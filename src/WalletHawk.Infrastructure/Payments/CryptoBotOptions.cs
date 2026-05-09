namespace WalletHawk.Infrastructure.Payments;

public sealed class CryptoBotOptions
{
    public const string SectionName = "CryptoBot";

    /// <summary>App token from @CryptoBot → Crypto Pay → Create App.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Mainnet: https://pay.crypt.bot   Testnet: https://testnet-pay.crypt.bot</summary>
    public string BaseUrl { get; set; } = "https://pay.crypt.bot";

    /// <summary>Monthly Pro plan price (in USDT).</summary>
    public decimal ProMonthlyPriceUsdt { get; set; } = 9.99m;

    /// <summary>Yearly Pro plan price (in USDT). Default ≈ -33% off vs 12 monthly.</summary>
    public decimal ProYearlyPriceUsdt { get; set; } = 79.99m;

    /// <summary>How long monthly Pro lasts after payment (days).</summary>
    public int ProMonthlyDurationDays { get; set; } = 30;

    /// <summary>How long yearly Pro lasts after payment (days).</summary>
    public int ProYearlyDurationDays { get; set; } = 365;

    /// <summary>Optional shared secret to validate webhook signatures.</summary>
    public string? WebhookSecret { get; set; }
}
