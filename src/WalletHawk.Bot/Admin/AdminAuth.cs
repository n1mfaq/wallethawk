using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WalletHawk.Bot.MiniApp;
using WalletHawk.Bot.Options;
using WalletHawk.Bot.Services;

namespace WalletHawk.Bot.Admin;

/// <summary>
/// Authorize an admin caller via either:
///   1) Telegram WebApp initData (X-Telegram-Init-Data header) + IsAdmin(userId), or
///   2) X-Admin-Token header / ?token=… query string equal to Bot:AdminToken.
/// Both result in HTTP 401 if missing/invalid.
/// </summary>
public static class AdminAuth
{
    public const string InitDataHeader = "X-Telegram-Init-Data";
    public const string TokenHeader = "X-Admin-Token";

    public static bool Authorize(HttpContext ctx, IConfiguration cfg, AdminService admin, IOptions<BotOptions> opt)
    {
        // 1) Token header or query string
        var token = ctx.Request.Headers[TokenHeader].ToString();
        if (string.IsNullOrEmpty(token))
            token = ctx.Request.Query["token"].ToString();
        if (!string.IsNullOrEmpty(token))
        {
            var expected = opt.Value.AdminToken;
            if (!string.IsNullOrEmpty(expected) && FixedTimeEquals(token, expected))
                return true;
        }

        // 2) Telegram initData
        var initData = ctx.Request.Headers[InitDataHeader].ToString();
        if (!string.IsNullOrEmpty(initData))
        {
            var botToken = cfg["Bot:Token"] ?? "";
            var result = TelegramInitData.Validate(initData, botToken);
            if (result.IsValid && result.User is not null && admin.IsAdmin(result.User.Id))
                return true;
        }

        return false;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
