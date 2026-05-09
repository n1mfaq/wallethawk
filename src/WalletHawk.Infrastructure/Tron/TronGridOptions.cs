namespace WalletHawk.Infrastructure.Tron;

public sealed class TronGridOptions
{
    public const string SectionName = "TronGrid";

    /// <summary>API base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.trongrid.io";

    /// <summary>Optional API key (free tier is fine without one for low load).</summary>
    public string? ApiKey { get; set; }

    /// <summary>USDT TRC20 contract address.</summary>
    public string UsdtContract { get; set; } = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
}
