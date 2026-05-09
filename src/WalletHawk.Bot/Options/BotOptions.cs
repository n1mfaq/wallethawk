namespace WalletHawk.Bot.Options;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Telegram bot API token from @BotFather.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Free plan: how many wallets a user can add without paying.</summary>
    public int FreeWalletLimit { get; set; } = 1;

    /// <summary>Telegram username (without @) of the bot owner — for upgrade/contact link.</summary>
    public string OwnerUsername { get; set; } = "cwiwi9";

    /// <summary>HTTPS URL of the Mini App (Telegram WebApp). Must be HTTPS and publicly reachable.</summary>
    public string WebAppUrl { get; set; } = "https://n1mfaq.github.io/wallethawk/app/";
}
