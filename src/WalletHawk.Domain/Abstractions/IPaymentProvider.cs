namespace WalletHawk.Domain.Abstractions;

public interface IPaymentProvider
{
    /// <summary>Create an invoice and return its checkout URL.</summary>
    /// <param name="telegramUserId">Telegram user id; encoded into the provider's <c>payload</c> field.</param>
    /// <param name="amount">Amount in USDT.</param>
    /// <param name="description">User-visible invoice description.</param>
    /// <param name="planTag">Short tag (e.g. "monthly" or "yearly") forwarded back via the webhook payload.</param>
    Task<PaymentInvoice> CreateInvoiceAsync(
        long telegramUserId,
        decimal amount,
        string description,
        string planTag,
        CancellationToken ct = default);
}

public sealed record PaymentInvoice(string InvoiceId, string PayUrl);
