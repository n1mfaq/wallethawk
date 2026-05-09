using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Infrastructure.Payments;

namespace WalletHawk.Bot.Payments;

public static class CryptoBotWebhook
{
    public static async Task<IResult> HandleAsync(
        HttpContext ctx,
        PaymentService payments,
        INotifier notifier,
        IOptions<CryptoBotOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("CryptoBotWebhook");

        using var sr = new StreamReader(ctx.Request.Body);
        var raw = await sr.ReadToEndAsync(ct);

        if (!VerifySignature(ctx.Request.Headers["crypto-pay-api-signature"].ToString(),
                             raw, options.Value.ApiKey))
        {
            log.LogWarning("CryptoBot webhook signature mismatch");
            return Results.Unauthorized();
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var updateType = root.GetProperty("update_type").GetString();
            if (updateType != "invoice_paid") return Results.Ok();

            var payload = root.GetProperty("payload");
            // CryptoBot returns the original invoice; user id was stored in `payload`
            var userIdStr = payload.GetProperty("payload").GetString();
            if (!long.TryParse(userIdStr, out var telegramUserId))
            {
                log.LogWarning("Invalid payload in CryptoBot webhook: {Payload}", userIdStr);
                return Results.Ok();
            }

            var ok = await payments.ActivateProAsync(telegramUserId, ct);
            if (ok)
            {
                await notifier.NotifyAsync(telegramUserId,
                    "✅ *Payment received*\\. WalletHawk *Pro* is active 🦅",
                    ct);
                log.LogInformation("Activated Pro for user {UserId}", telegramUserId);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to process CryptoBot webhook");
        }

        return Results.Ok();
    }

    private static bool VerifySignature(string headerSignature, string body, string apiKey)
    {
        if (string.IsNullOrEmpty(headerSignature) || string.IsNullOrEmpty(apiKey)) return false;
        // CryptoBot uses HMAC-SHA256 with secret = SHA256(apiKey)
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(headerSignature.ToLowerInvariant()));
    }
}
