namespace WalletHawk.Domain.Entities;

public enum TxDirection
{
    In = 1,
    Out = 2,
}

/// <summary>Persisted record of a TRC20 transfer that the bot already notified the user about.</summary>
public class Transaction
{
    public long Id { get; set; }

    public long WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;

    /// <summary>On-chain transaction hash (unique per wallet).</summary>
    public string TxHash { get; set; } = string.Empty;

    public TxDirection Direction { get; set; }

    /// <summary>Token amount (e.g. 4.99). Stored as numeric(38,18) to fit any token decimals safely.</summary>
    public decimal Amount { get; set; }

    public string TokenSymbol { get; set; } = "USDT";

    /// <summary>Counterparty address (sender if IN, recipient if OUT).</summary>
    public string Counterparty { get; set; } = string.Empty;

    /// <summary>On-chain block timestamp.</summary>
    public DateTimeOffset BlockTime { get; set; }

    /// <summary>When the bot recorded this row.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
