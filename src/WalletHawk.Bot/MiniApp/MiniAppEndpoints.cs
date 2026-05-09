using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WalletHawk.Bot.Options;
using WalletHawk.Data;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Bot.MiniApp;

public static class MiniAppEndpoints
{
    /// <summary>Header used by the Mini App to send the Telegram initData payload.</summary>
    public const string InitDataHeader = "X-Telegram-Init-Data";

    public static IEndpointRouteBuilder MapMiniApp(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me");

        group.MapGet("", GetMeAsync);
        group.MapGet("/wallets", GetWalletsAsync);
        group.MapGet("/transactions", GetTransactionsAsync);
        group.MapGet("/stats", GetStatsAsync);

        return app;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static (long? telegramUserId, IResult? error) Authenticate(HttpContext ctx, IConfiguration cfg)
    {
        var initData = ctx.Request.Headers[InitDataHeader].ToString();
        if (string.IsNullOrEmpty(initData))
            return (null, Results.Json(new { error = "missing initData" }, statusCode: 401));

        var token = cfg["Bot:Token"];
        var result = TelegramInitData.Validate(initData, token ?? "");
        if (!result.IsValid || result.User is null)
            return (null, Results.Json(new { error = result.Error ?? "invalid" }, statusCode: 401));

        return (result.User.Id, null);
    }

    // ── GET /api/me ─────────────────────────────────────────────────────────
    private static async Task<IResult> GetMeAsync(
        HttpContext ctx, IConfiguration cfg, AppDbContext db, CancellationToken ct)
    {
        var (id, err) = Authenticate(ctx, cfg);
        if (err is not null) return err;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        if (user is null)
            return Results.Json(new { error = "user not found — open the bot once to register" }, statusCode: 404);

        var walletCount = await db.Wallets.CountAsync(w => w.UserId == user.Id, ct);

        return Results.Json(new
        {
            telegramUserId = user.TelegramUserId,
            username = user.Username,
            firstName = user.FirstName,
            createdAt = user.CreatedAt,
            isPro = user.IsPro,
            proUntil = user.ProUntil,
            walletCount,
        });
    }

    // ── GET /api/me/wallets ─────────────────────────────────────────────────
    private static async Task<IResult> GetWalletsAsync(
        HttpContext ctx, IConfiguration cfg, AppDbContext db, CancellationToken ct)
    {
        var (id, err) = Authenticate(ctx, cfg);
        if (err is not null) return err;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        if (user is null) return Results.Json(Array.Empty<object>());

        var wallets = await db.Wallets.AsNoTracking()
            .Where(w => w.UserId == user.Id)
            .OrderBy(w => w.Id)
            .Select(w => new
            {
                id = w.Id,
                network = w.Network.ToString(),
                address = w.Address,
                label = w.Label,
                createdAt = w.CreatedAt,
                lastCheckedAt = w.LastCheckedAt,
                lastTxHash = w.LastTxHash,
            })
            .ToListAsync(ct);

        return Results.Json(wallets);
    }

    // ── GET /api/me/transactions?days=7&walletId= ───────────────────────────
    private static async Task<IResult> GetTransactionsAsync(
        HttpContext ctx, IConfiguration cfg, AppDbContext db, CancellationToken ct)
    {
        var (id, err) = Authenticate(ctx, cfg);
        if (err is not null) return err;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        if (user is null) return Results.Json(Array.Empty<object>());

        var days = ParseInt(ctx.Request.Query["days"], 7, min: 1, max: 90);
        var walletIdStr = ctx.Request.Query["walletId"].ToString();
        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.Wallet.UserId == user.Id && t.BlockTime >= since);

        if (long.TryParse(walletIdStr, out var walletId))
            query = query.Where(t => t.WalletId == walletId);

        var rows = await query
            .OrderByDescending(t => t.BlockTime)
            .Take(200)
            .Select(t => new
            {
                id = t.Id,
                walletId = t.WalletId,
                walletLabel = t.Wallet.Label,
                walletAddress = t.Wallet.Address,
                txHash = t.TxHash,
                direction = t.Direction == TxDirection.In ? "in" : "out",
                amount = t.Amount,
                token = t.TokenSymbol,
                counterparty = t.Counterparty,
                blockTime = t.BlockTime,
            })
            .ToListAsync(ct);

        return Results.Json(rows);
    }

    // ── GET /api/me/stats?days=7 ────────────────────────────────────────────
    private static async Task<IResult> GetStatsAsync(
        HttpContext ctx, IConfiguration cfg, AppDbContext db, CancellationToken ct)
    {
        var (id, err) = Authenticate(ctx, cfg);
        if (err is not null) return err;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        if (user is null) return Results.Json(new { totalIn = 0m, totalOut = 0m, byDay = Array.Empty<object>() });

        var days = ParseInt(ctx.Request.Query["days"], 7, min: 1, max: 90);
        var since = DateTimeOffset.UtcNow.AddDays(-days).Date;

        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Wallet.UserId == user.Id && t.BlockTime >= since)
            .Select(t => new { t.BlockTime, t.Direction, t.Amount })
            .ToListAsync(ct);

        var totalIn = rows.Where(r => r.Direction == TxDirection.In).Sum(r => r.Amount);
        var totalOut = rows.Where(r => r.Direction == TxDirection.Out).Sum(r => r.Amount);

        var byDay = rows
            .GroupBy(r => r.BlockTime.UtcDateTime.Date)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                date = g.Key.ToString("yyyy-MM-dd"),
                @in = g.Where(x => x.Direction == TxDirection.In).Sum(x => x.Amount),
                @out = g.Where(x => x.Direction == TxDirection.Out).Sum(x => x.Amount),
            })
            .ToList();

        return Results.Json(new { totalIn, totalOut, byDay });
    }

    private static int ParseInt(Microsoft.Extensions.Primitives.StringValues v, int @default, int min, int max)
    {
        if (int.TryParse(v.ToString(), out var n))
            return Math.Clamp(n, min, max);
        return @default;
    }
}
