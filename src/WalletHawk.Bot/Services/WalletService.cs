using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WalletHawk.Bot.Options;
using WalletHawk.Data;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Bot.Services;

public sealed class WalletService
{
    private readonly AppDbContext _db;
    private readonly BotOptions _opt;

    public WalletService(AppDbContext db, IOptions<BotOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    public Task<List<Wallet>> ListAsync(long userId, CancellationToken ct = default) =>
        _db.Wallets.AsNoTracking().Where(w => w.UserId == userId)
            .OrderBy(w => w.Id)
            .ToListAsync(ct);

    public async Task<int> CountAsync(long userId, CancellationToken ct = default) =>
        await _db.Wallets.CountAsync(w => w.UserId == userId, ct);

    public async Task<AddResult> AddAsync(User user, WalletNetwork network, string address, string? label, CancellationToken ct = default)
    {
        var existing = await _db.Wallets.AnyAsync(w =>
            w.UserId == user.Id && w.Network == network && w.Address == address, ct);
        if (existing) return AddResult.AlreadyExists;

        var count = await CountAsync(user.Id, ct);
        if (!user.IsPro && count >= _opt.FreeWalletLimit)
            return AddResult.LimitReached;

        _db.Wallets.Add(new Wallet
        {
            UserId = user.Id,
            Network = network,
            Address = address,
            Label = label,
        });
        await _db.SaveChangesAsync(ct);
        return AddResult.Ok;
    }

    public async Task<bool> RemoveAsync(long userId, long walletId, CancellationToken ct = default)
    {
        var w = await _db.Wallets.FirstOrDefaultAsync(x => x.Id == walletId && x.UserId == userId, ct);
        if (w is null) return false;
        _db.Wallets.Remove(w);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public enum AddResult { Ok, AlreadyExists, LimitReached }
