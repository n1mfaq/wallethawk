using Microsoft.EntityFrameworkCore;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.FirstName).HasMaxLength(128);
        });

        b.Entity<Wallet>(e =>
        {
            e.ToTable("wallets");
            e.Property(x => x.Address).HasMaxLength(64).IsRequired();
            e.Property(x => x.Label).HasMaxLength(64);
            e.Property(x => x.LastTxHash).HasMaxLength(80);
            e.HasIndex(x => new { x.UserId, x.Network, x.Address }).IsUnique();
            e.HasIndex(x => new { x.Network, x.Address });

            e.HasOne(x => x.User)
                .WithMany(x => x.Wallets)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.Property(x => x.TxHash).HasMaxLength(80).IsRequired();
            e.Property(x => x.Counterparty).HasMaxLength(64).IsRequired();
            e.Property(x => x.TokenSymbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Amount).HasPrecision(38, 18);

            // Same on-chain tx is unique per wallet (prevents double-recording on retries).
            e.HasIndex(x => new { x.WalletId, x.TxHash }).IsUnique();
            // Common queries: timeline of a wallet by time, dashboard timeline by user.
            e.HasIndex(x => new { x.WalletId, x.BlockTime });

            e.HasOne(x => x.Wallet)
                .WithMany()
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
