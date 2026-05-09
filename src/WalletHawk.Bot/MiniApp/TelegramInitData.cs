using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace WalletHawk.Bot.MiniApp;

/// <summary>
/// Validates and parses the <c>initData</c> string that Telegram passes to a Mini App.
/// Spec: https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public static class TelegramInitData
{
    public sealed record InitDataUser(
        long Id,
        string? Username,
        string? FirstName,
        string? LastName,
        string? LanguageCode);

    public sealed record InitDataResult(
        bool IsValid,
        InitDataUser? User,
        DateTimeOffset? AuthDate,
        string? Error);

    /// <summary>How long an initData payload is accepted after the auth_date.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static InitDataResult Validate(string? initData, string botToken)
    {
        if (string.IsNullOrWhiteSpace(initData))
            return new(false, null, null, "missing initData");
        if (string.IsNullOrWhiteSpace(botToken))
            return new(false, null, null, "missing bot token");

        // Parse query-string-like format
        var pairs = HttpUtility.ParseQueryString(initData);
        var hash = pairs["hash"];
        if (string.IsNullOrEmpty(hash))
            return new(false, null, null, "missing hash");

        // Build data-check-string: every key=value joined by \n, sorted alphabetically, except `hash`.
        var dataCheck = pairs.AllKeys
            .Where(k => k is not null && k != "hash")
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k}={pairs[k!]}")
            .ToArray();
        var dataCheckString = string.Join('\n', dataCheck);

        // secret = HMAC-SHA256(botToken, key="WebAppData")
        var secret = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));

        // expected = HMAC-SHA256(dataCheckString, key=secret) → hex
        var expectedBytes = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString));
        var expectedHex = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(hash.ToLowerInvariant()));
        if (!ok) return new(false, null, null, "bad signature");

        // Freshness check
        DateTimeOffset? authDate = null;
        var authDateRaw = pairs["auth_date"];
        if (long.TryParse(authDateRaw, out var unix))
        {
            authDate = DateTimeOffset.FromUnixTimeSeconds(unix);
            if (DateTimeOffset.UtcNow - authDate > MaxAge)
                return new(false, null, authDate, "expired");
        }

        // Parse user object
        InitDataUser? user = null;
        var userJson = pairs["user"];
        if (!string.IsNullOrEmpty(userJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(userJson);
                var root = doc.RootElement;
                user = new InitDataUser(
                    Id: root.GetProperty("id").GetInt64(),
                    Username: root.TryGetProperty("username", out var un) ? un.GetString() : null,
                    FirstName: root.TryGetProperty("first_name", out var fn) ? fn.GetString() : null,
                    LastName: root.TryGetProperty("last_name", out var ln) ? ln.GetString() : null,
                    LanguageCode: root.TryGetProperty("language_code", out var lc) ? lc.GetString() : null);
            }
            catch (JsonException)
            {
                return new(false, null, authDate, "bad user payload");
            }
        }

        return new(true, user, authDate, null);
    }
}
