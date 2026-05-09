using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WalletHawk.Bot.Options;
using WalletHawk.Data;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Bot.Services;

public sealed class AdminService
{
    private readonly AppDbContext _db;
    private readonly BotOptions _opt;

    public AdminService(AppDbContext db, IOptions<BotOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    /// <summary>Authorize by numeric Telegram user id — username can change/be released.</summary>
    public bool IsAdmin(long telegramUserId) =>
        _opt.OwnerTelegramId != 0 && telegramUserId == _opt.OwnerTelegramId;

    public async Task<AdminStats> GetStatsAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.CountAsync(ct);
        var pro = await _db.Users.CountAsync(u => u.IsPro, ct);
        var wallets = await _db.Wallets.CountAsync(ct);

        var since24h = DateTimeOffset.UtcNow.AddDays(-1);
        var newUsers24h = await _db.Users.CountAsync(u => u.CreatedAt >= since24h, ct);
        var newWallets24h = await _db.Wallets.CountAsync(w => w.CreatedAt >= since24h, ct);

        return new AdminStats(users, pro, wallets, newUsers24h, newWallets24h);
    }

    public async Task<User?> FindUserAsync(string usernameOrId, CancellationToken ct = default)
    {
        var trimmed = usernameOrId.TrimStart('@');
        if (long.TryParse(trimmed, out var id))
        {
            var byId = await _db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
            if (byId is not null) return byId;
        }
        return await _db.Users.FirstOrDefaultAsync(
            u => u.Username != null && u.Username.ToLower() == trimmed.ToLower(), ct);
    }

    public async Task<User?> GrantProAsync(string usernameOrId, int days, CancellationToken ct = default)
    {
        var user = await FindUserAsync(usernameOrId, ct);
        return user is null ? null : await GrantProCoreAsync(user, days, ct);
    }

    public async Task<User?> GrantProByIdAsync(long internalUserId, int days, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == internalUserId, ct);
        return user is null ? null : await GrantProCoreAsync(user, days, ct);
    }

    private async Task<User> GrantProCoreAsync(User user, int days, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var current = user.ProUntil is { } u && u > now ? u : now;
        user.ProUntil = current.AddDays(days);
        user.IsPro = true;
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User?> RevokeProAsync(string usernameOrId, CancellationToken ct = default)
    {
        var user = await FindUserAsync(usernameOrId, ct);
        return user is null ? null : await RevokeProCoreAsync(user, ct);
    }

    public async Task<User?> RevokeProByIdAsync(long internalUserId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == internalUserId, ct);
        return user is null ? null : await RevokeProCoreAsync(user, ct);
    }

    private async Task<User> RevokeProCoreAsync(User user, CancellationToken ct)
    {
        user.IsPro = false;
        user.ProUntil = null;
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public Task<List<long>> GetAllTelegramIdsAsync(CancellationToken ct = default) =>
        _db.Users.Select(u => u.TelegramUserId).ToListAsync(ct);

    public Task<List<Wallet>> GetWalletsAsync(long userId, CancellationToken ct = default) =>
        _db.Wallets.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.Id)
            .ToListAsync(ct);

    /// <summary>Per-day new-user counts for the last <paramref name="days"/> days, oldest first.</summary>
    public async Task<List<DailyCount>> GetDailyNewUsersAsync(int days, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days).Date;
        var rows = await _db.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= since)
            .Select(u => u.CreatedAt)
            .ToListAsync(ct);

        return rows
            .GroupBy(d => d.UtcDateTime.Date)
            .Select(g => new DailyCount(g.Key.ToString("yyyy-MM-dd"), g.Count()))
            .OrderBy(d => d.Date)
            .ToList();
    }

    /// <summary>Per-day transaction counts for the last <paramref name="days"/> days, oldest first.</summary>
    public async Task<List<DailyCount>> GetDailyTransactionsAsync(int days, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days).Date;
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.BlockTime >= since)
            .Select(t => t.BlockTime)
            .ToListAsync(ct);

        return rows
            .GroupBy(d => d.UtcDateTime.Date)
            .Select(g => new DailyCount(g.Key.ToString("yyyy-MM-dd"), g.Count()))
            .OrderBy(d => d.Date)
            .ToList();
    }

    public sealed record UserListRow(
        long Id,
        long TelegramUserId,
        string? Username,
        string? FirstName,
        bool IsPro,
        DateTimeOffset? ProUntil,
        DateTimeOffset CreatedAt,
        int WalletCount);

    /// <summary>List users with optional search; returns up to 200 rows.</summary>
    public async Task<List<UserListRow>> ListUsersAsync(string? search, CancellationToken ct = default)
    {
        var q = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.TrimStart('@').ToLower();
            q = q.Where(u =>
                (u.Username != null && u.Username.ToLower().Contains(s)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                u.TelegramUserId.ToString().Contains(s));
        }

        return await q
            .OrderByDescending(u => u.CreatedAt)
            .Take(200)
            .Select(u => new UserListRow(
                u.Id,
                u.TelegramUserId,
                u.Username,
                u.FirstName,
                u.IsPro,
                u.ProUntil,
                u.CreatedAt,
                _db.Wallets.Count(w => w.UserId == u.Id)))
            .ToListAsync(ct);
    }
}

public sealed record DailyCount(string Date, int Count);

public sealed record AdminStats(
    int Users,
    int Pro,
    int Wallets,
    int NewUsers24h,
    int NewWallets24h);
