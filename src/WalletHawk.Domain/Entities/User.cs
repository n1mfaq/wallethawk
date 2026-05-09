namespace WalletHawk.Domain.Entities;

public class User
{
    public long Id { get; set; }                // PK
    public long TelegramUserId { get; set; }    // unique
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsPro { get; set; }
    public DateTimeOffset? ProUntil { get; set; }

    public ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();
}
