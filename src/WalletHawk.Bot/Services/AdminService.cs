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

    public bool IsAdmin(string? username) =>
        !string.IsNullOrEmpty(username)
        && string.Equals(username, _opt.OwnerUsername, StringComparison.OrdinalIgnoreCase);

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
        if (user is null) return null;

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
        if (user is null) return null;

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
}

public sealed record AdminStats(
    int Users,
    int Pro,
    int Wallets,
    int NewUsers24h,
    int NewWallets24h);
