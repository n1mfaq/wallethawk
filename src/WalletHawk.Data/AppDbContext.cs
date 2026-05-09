using Microsoft.EntityFrameworkCore;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Wallet> Wallets => Set<Wallet>();

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
    }
}
