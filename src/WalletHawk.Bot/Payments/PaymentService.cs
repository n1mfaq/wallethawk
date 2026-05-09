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

    public Task<PaymentInvoice> CreateMonthlyInvoiceAsync(long telegramUserId, CancellationToken ct = default) =>
        _provider.CreateInvoiceAsync(
            telegramUserId,
            _opt.ProMonthlyPriceUsdt,
            $"WalletHawk Pro · {_opt.ProMonthlyDurationDays} days",
            "monthly",
            ct);

    public Task<PaymentInvoice> CreateYearlyInvoiceAsync(long telegramUserId, CancellationToken ct = default) =>
        _provider.CreateInvoiceAsync(
            telegramUserId,
            _opt.ProYearlyPriceUsdt,
            $"WalletHawk Pro · {_opt.ProYearlyDurationDays} days",
            "yearly",
            ct);

    /// <summary>Activate Pro after a successful payment, extending an existing subscription if any.</summary>
    public async Task<bool> ActivateProAsync(long telegramUserId, string planTag, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);
        if (user is null) return false;

        var days = string.Equals(planTag, "yearly", StringComparison.OrdinalIgnoreCase)
            ? _opt.ProYearlyDurationDays
            : _opt.ProMonthlyDurationDays;

        var now = DateTimeOffset.UtcNow;
        var current = user.ProUntil is { } u && u > now ? u : now;
        user.ProUntil = current.AddDays(days);
        user.IsPro = true;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
