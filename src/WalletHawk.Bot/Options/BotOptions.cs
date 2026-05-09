namespace WalletHawk.Bot.Options;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Telegram bot API token from @BotFather.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Free plan: how many wallets a user can add without paying.</summary>
    public int FreeWalletLimit { get; set; } = 2;

    /// <summary>Telegram username (without @) of the bot owner — for upgrade/contact link.</summary>
    public string OwnerUsername { get; set; } = "cwiwi9";

    /// <summary>
    /// Telegram numeric user id of the bot owner — used for admin authorization.
    /// Doesn't change if the user changes/removes their username, so it's safer than OwnerUsername.
    /// </summary>
    public long OwnerTelegramId { get; set; } = 6717364079;

    /// <summary>HTTPS URL of the Mini App (Telegram WebApp). Must be HTTPS and publicly reachable.</summary>
    public string WebAppUrl { get; set; } = "https://n1mfaq.github.io/wallethawk/app/";

    /// <summary>HTTPS URL of the admin panel (served by this bot). Used by /panel command.</summary>
    public string AdminWebAppUrl { get; set; } = "https://wallethawk-bot.fly.dev/admin/";

    /// <summary>
    /// Optional shared secret for accessing /admin/ from a regular browser (without Telegram).
    /// Pass via X-Admin-Token header or ?token=… query string.
    /// </summary>
    public string AdminToken { get; set; } = "";
}
