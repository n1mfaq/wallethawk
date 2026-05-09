namespace WalletHawk.Domain.Entities;

public enum WalletNetwork
{
    TronTrc20 = 1,
}

public class Wallet
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public WalletNetwork Network { get; set; } = WalletNetwork.TronTrc20;
    public string Address { get; set; } = string.Empty;
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last seen tx hash on this wallet (for delta polling).</summary>
    public string? LastTxHash { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
}
