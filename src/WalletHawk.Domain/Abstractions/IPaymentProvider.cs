namespace WalletHawk.Domain.Abstractions;

public interface IPaymentProvider
{
    /// <summary>Create an invoice and return its checkout URL.</summary>
    Task<PaymentInvoice> CreateInvoiceAsync(long telegramUserId, string description, CancellationToken ct = default);
}

public sealed record PaymentInvoice(string InvoiceId, string PayUrl);
