using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using WalletHawk.Domain.Abstractions;

namespace WalletHawk.Infrastructure.Telegram;

public sealed class TelegramNotifier : INotifier
{
    private readonly ITelegramBotClient _bot;

    public TelegramNotifier(ITelegramBotClient bot) => _bot = bot;

    public async Task NotifyAsync(long telegramUserId, string markdown, CancellationToken ct = default)
    {
        await _bot.SendMessage(telegramUserId, markdown, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }
}
