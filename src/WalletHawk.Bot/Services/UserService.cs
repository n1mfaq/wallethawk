using Microsoft.EntityFrameworkCore;
using WalletHawk.Data;
using WalletHawk.Domain.Entities;

namespace WalletHawk.Bot.Services;

public sealed class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db) => _db = db;

    public async Task<User> GetOrCreateAsync(long telegramUserId, string? username, string? firstName, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);
        if (user is not null)
        {
            // keep contact info fresh
            if (user.Username != username || user.FirstName != firstName)
            {
                user.Username = username;
                user.FirstName = firstName;
                await _db.SaveChangesAsync(ct);
            }
            return user;
        }

        user = new User
        {
            TelegramUserId = telegramUserId,
            Username = username,
            FirstName = firstName,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
