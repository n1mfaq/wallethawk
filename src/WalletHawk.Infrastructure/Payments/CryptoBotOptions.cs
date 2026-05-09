namespace WalletHawk.Infrastructure.Payments;

public sealed class CryptoBotOptions
{
    public const string SectionName = "CryptoBot";

    /// <summary>App token from @CryptoBot → Crypto Pay → Create App.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Mainnet: https://pay.crypt.bot   Testnet: https://testnet-pay.crypt.bot</summary>
    public string BaseUrl { get; set; } = "https://pay.crypt.bot";

    /// <summary>Pro plan price (in USDT).</summary>
    public decimal ProPriceUsdt { get; set; } = 4.99m;

    /// <summary>How long Pro lasts after payment (days).</summary>
    public int ProDurationDays { get; set; } = 30;

    /// <summary>Optional shared secret to validate webhook signatures.</summary>
    public string? WebhookSecret { get; set; }
}
