using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using WalletHawk.Bot.Options;
using WalletHawk.Bot.Services;
using WalletHawk.Data;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Bot.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin");

        g.MapGet("/whoami", WhoAmI);
        g.MapGet("/overview", Overview);
        g.MapGet("/users", ListUsers);
        g.MapGet("/users/{id:long}", UserDetail);
        g.MapPost("/users/{id:long}/grant", GrantPro);
        g.MapPost("/users/{id:long}/revoke", RevokePro);
        g.MapPost("/broadcast", Broadcast);

        return app;
    }

    private static IResult? AuthOrFail(HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt)
    {
        if (AdminAuth.Authorize(ctx, cfg, admin, opt)) return null;
        return Results.Json(new { error = "unauthorized" }, statusCode: 401);
    }

    // ── GET /api/admin/whoami ───────────────────────────────────────────
    private static IResult WhoAmI(
        HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        return err ?? Results.Json(new { ok = true });
    }

    // ── GET /api/admin/overview ─────────────────────────────────────────
    private static async Task<IResult> Overview(
        HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt, AppDbContext db, CancellationToken ct)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        if (err is not null) return err;

        var s = await admin.GetStatsAsync(ct);
        var newUsersByDay = await admin.GetDailyNewUsersAsync(30, ct);
        var txByDay = await admin.GetDailyTransactionsAsync(30, ct);
        var totalTx = await db.Transactions.CountAsync(ct);

        return Results.Json(new
        {
            users = s.Users,
            pro = s.Pro,
            wallets = s.Wallets,
            newUsers24h = s.NewUsers24h,
            newWallets24h = s.NewWallets24h,
            mrr = s.Pro * 4.99m,
            totalTx,
            newUsersByDay,
            txByDay,
        });
    }

    // ── GET /api/admin/users?q=foo ──────────────────────────────────────
    private static async Task<IResult> ListUsers(
        HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt, CancellationToken ct)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        if (err is not null) return err;

        var q = ctx.Request.Query["q"].ToString();
        var rows = await admin.ListUsersAsync(string.IsNullOrWhiteSpace(q) ? null : q, ct);
        return Results.Json(rows);
    }

    // ── GET /api/admin/users/{id} ───────────────────────────────────────
    private static async Task<IResult> UserDetail(
        long id, HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt,
        AppDbContext db, CancellationToken ct)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        if (err is not null) return err;

        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return Results.NotFound();

        var wallets = await db.Wallets.AsNoTracking()
            .Where(w => w.UserId == u.Id)
            .OrderBy(w => w.Id)
            .Select(w => new
            {
                w.Id,
                Network = w.Network.ToString(),
                w.Address,
                w.Label,
                w.CreatedAt,
                w.LastCheckedAt,
            })
            .ToListAsync(ct);

        var txs = await db.Transactions.AsNoTracking()
            .Where(t => t.Wallet.UserId == u.Id)
            .OrderByDescending(t => t.BlockTime)
            .Take(50)
            .Select(t => new
            {
                t.Id,
                t.WalletId,
                t.TxHash,
                Direction = t.Direction == TxDirection.In ? "in" : "out",
                t.Amount,
                Token = t.TokenSymbol,
                t.Counterparty,
                t.BlockTime,
            })
            .ToListAsync(ct);

        return Results.Json(new
        {
            user = new
            {
                u.Id,
                u.TelegramUserId,
                u.Username,
                u.FirstName,
                u.CreatedAt,
                u.IsPro,
                u.ProUntil,
            },
            wallets,
            transactions = txs,
        });
    }

    // ── POST /api/admin/users/{id}/grant?days=30 ───────────────────────
    private static async Task<IResult> GrantPro(
        long id, HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt,
        ITelegramBotClient bot, ILoggerFactory lf, CancellationToken ct)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        if (err is not null) return err;

        var days = int.TryParse(ctx.Request.Query["days"], out var d) && d > 0 ? d : 30;

        var user = await admin.GrantProAsync(id.ToString(), days, ct);
        if (user is null) return Results.NotFound();

        // Best-effort notification
        try
        {
            await bot.SendMessage(user.TelegramUserId,
                $"🎁 <b>WalletHawk Pro</b> has been granted to you for <b>{days} days</b>.\nEnjoy unlimited wallets 🦅",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            lf.CreateLogger("AdminApi.GrantPro")
                .LogWarning(ex, "Failed to notify {Id} about granted Pro", user.TelegramUserId);
        }

        return Results.Json(new { ok = true, isPro = user.IsPro, proUntil = user.ProUntil });
    }

    // ── POST /api/admin/users/{id}/revoke ──────────────────────────────
    private static async Task<IResult> RevokePro(
        long id, HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt,
        CancellationToken ct)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        if (err is not null) return err;

        var user = await admin.RevokeProAsync(id.ToString(), ct);
        if (user is null) return Results.NotFound();
        return Results.Json(new { ok = true, isPro = user.IsPro });
    }

    public sealed record BroadcastBody(string Message);

    // ── POST /api/admin/broadcast {message} ─────────────────────────────
    private static async Task<IResult> Broadcast(
        BroadcastBody body, HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt,
        ITelegramBotClient bot, ILoggerFactory lf, CancellationToken ct)
    {
        var err = AuthOrFail(ctx, cfg, admin, opt);
        if (err is not null) return err;

        if (string.IsNullOrWhiteSpace(body?.Message))
            return Results.BadRequest(new { error = "message is required" });

        var ids = await admin.GetAllTelegramIdsAsync(ct);
        var ok = 0;
        var failed = 0;
        var log = lf.CreateLogger("AdminApi.Broadcast");

        foreach (var id in ids)
        {
            try
            {
                await bot.SendMessage(id, body.Message, cancellationToken: ct);
                ok++;
                await Task.Delay(50, ct);
            }
            catch (Exception ex)
            {
                failed++;
                log.LogWarning(ex, "Broadcast failed for {Id}", id);
            }
        }

        return Results.Json(new { ok, failed, total = ids.Count });
    }
}
