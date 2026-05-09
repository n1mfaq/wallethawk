using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WalletHawk.Data;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Infrastructure.Payments;

namespace WalletHawk.Bot.Payments;

public sealed class PaymentService
{
    private readonly AppDbContext _db;
    private readonly IPaymentProvider _provider;
    private readonly CryptoBotOptions _opt;

    public PaymentService(AppDbContext db, IPaymentProvider provider, IOptions<CryptoBotOptions> opt)
    {
        _db = db;
        _provider = provider;
        _opt = opt.Value;
    }

    public Task<PaymentInvoice> CreateProInvoiceAsync(long telegramUserId, CancellationToken ct = default) =>
        _provider.CreateInvoiceAsync(telegramUserId, $"WalletHawk Pro · {_opt.ProDurationDays} days", ct);

    public async Task<bool> ActivateProAsync(long telegramUserId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);
        if (user is null) return false;

        var now = DateTimeOffset.UtcNow;
        var current = user.ProUntil is { } u && u > now ? u : now;
        user.ProUntil = current.AddDays(_opt.ProDurationDays);
        user.IsPro = true;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
