using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WalletHawk.Bot.Options;
using WalletHawk.Bot.Services;
using WalletHawk.Domain.Services;
using DomainUser = WalletHawk.Domain.Entities.User;
using WalletNetwork = WalletHawk.Domain.Entities.WalletNetwork;

namespace WalletHawk.Bot.Handlers;

public sealed class UpdateHandler : IUpdateHandler
{
    private readonly UserService _users;
    private readonly WalletService _wallets;
    private readonly BotOptions _opt;
    private readonly ILogger<UpdateHandler> _log;

    public UpdateHandler(
        UserService users,
        WalletService wallets,
        IOptions<BotOptions> opt,
        ILogger<UpdateHandler> log)
    {
        _users = users;
        _wallets = wallets;
        _opt = opt.Value;
        _log = log;
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        _log.LogError(exception, "Telegram error ({Source})", source);
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } msg) return;
        if (msg.From is null || msg.Text is null) return;

        var text = msg.Text.Trim();
        var (cmd, args) = ParseCommand(text);

        var user = await _users.GetOrCreateAsync(msg.From.Id, msg.From.Username, msg.From.FirstName, ct);

        try
        {
            switch (cmd)
            {
                case "/start": await CmdStart(bot, msg, ct); break;
                case "/help": await CmdHelp(bot, msg, ct); break;
                case "/add": await CmdAdd(bot, msg, user, args, ct); break;
                case "/list": await CmdList(bot, msg, user, ct); break;
                case "/remove": await CmdRemove(bot, msg, user, args, ct); break;
                case "/upgrade": await CmdUpgrade(bot, msg, ct); break;
                case "/me": await CmdMe(bot, msg, user, ct); break;
                default:
                    await bot.SendMessage(msg.Chat.Id,
                        "Unknown command. Try /help.", cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to handle command {Cmd}", cmd);
            await bot.SendMessage(msg.Chat.Id, "Something went wrong. Try again.", cancellationToken: ct);
        }
    }

    private static (string cmd, string args) ParseCommand(string text)
    {
        if (!text.StartsWith('/')) return ("", text);
        var space = text.IndexOf(' ');
        if (space < 0) return (text.ToLowerInvariant(), "");
        var cmd = text[..space].ToLowerInvariant();
        // strip @botname suffix in groups
        var at = cmd.IndexOf('@');
        if (at > 0) cmd = cmd[..at];
        return (cmd, text[(space + 1)..].Trim());
    }

    private async Task CmdStart(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var welcome =
            "*WalletHawk* — TRC20 wallet tracker\n\n" +
            "I watch your Tron \\(TRC20\\) wallets and ping you whenever USDT moves in or out\\.\n\n" +
            "Commands:\n" +
            "`/add <address>` — track a wallet\n" +
            "`/list` — show tracked wallets\n" +
            "`/remove <id>` — stop tracking\n" +
            "`/me` — your status\n" +
            "`/upgrade` — go Pro\n" +
            "`/help` — show help";
        await bot.SendMessage(msg.Chat.Id, welcome, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }

    private async Task CmdHelp(ITelegramBotClient bot, Message msg, CancellationToken ct)
        => await CmdStart(bot, msg, ct);

    private async Task CmdAdd(ITelegramBotClient bot, Message msg, DomainUser user, string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(msg.Chat.Id,
                "Usage: `/add <TRC20_address> [label]`",
                parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
            return;
        }

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var address = parts[0];
        var label = parts.Length > 1 ? parts[1] : null;

        if (!TronAddress.IsValidTrc20(address))
        {
            await bot.SendMessage(msg.Chat.Id, "Not a valid TRC20 (Tron) address.", cancellationToken: ct);
            return;
        }

        var result = await _wallets.AddAsync(user, WalletNetwork.TronTrc20, address, label, ct);
        switch (result)
        {
            case AddResult.Ok:
                await bot.SendMessage(msg.Chat.Id, $"✅ Tracking `{TronAddress.Mask(address)}`",
                    parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
                break;
            case AddResult.AlreadyExists:
                await bot.SendMessage(msg.Chat.Id, "You're already tracking this address.", cancellationToken: ct);
                break;
            case AddResult.LimitReached:
                await bot.SendMessage(msg.Chat.Id,
                    $"Free plan allows {_opt.FreeWalletLimit} wallet(s). Use /upgrade to track more.",
                    cancellationToken: ct);
                break;
        }
    }

    private async Task CmdList(ITelegramBotClient bot, Message msg, DomainUser user, CancellationToken ct)
    {
        var wallets = await _wallets.ListAsync(user.Id, ct);
        if (wallets.Count == 0)
        {
            await bot.SendMessage(msg.Chat.Id, "No wallets yet. Add one with /add <address>.", cancellationToken: ct);
            return;
        }

        var lines = wallets.Select(w =>
            $"`{w.Id}` · `{TronAddress.Mask(w.Address)}`{(string.IsNullOrEmpty(w.Label) ? "" : $" — {w.Label}")}");
        var text = "*Your wallets:*\n" + string.Join('\n', lines);
        await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }

    private async Task CmdRemove(ITelegramBotClient bot, Message msg, DomainUser user, string args, CancellationToken ct)
    {
        if (!long.TryParse(args, out var id))
        {
            await bot.SendMessage(msg.Chat.Id, "Usage: /remove <id>  (id from /list)", cancellationToken: ct);
            return;
        }
        var ok = await _wallets.RemoveAsync(user.Id, id, ct);
        await bot.SendMessage(msg.Chat.Id, ok ? "🗑 Removed." : "Wallet not found.", cancellationToken: ct);
    }

    private async Task CmdMe(ITelegramBotClient bot, Message msg, DomainUser user, CancellationToken ct)
    {
        var count = await _wallets.CountAsync(user.Id, ct);
        var plan = user.IsPro ? "Pro" : "Free";
        var limit = user.IsPro ? "unlimited" : _opt.FreeWalletLimit.ToString();
        var text = $"Plan: *{plan}*\nWallets: {count} / {limit}";
        await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }

    private async Task CmdUpgrade(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var text =
            "*WalletHawk Pro* — $4\\.99/mo\n\n" +
            "• unlimited wallets\n" +
            "• instant alerts \\(every 30s\\)\n" +
            "• priority support\n\n" +
            $"DM @{_opt.OwnerUsername} to activate\\.";
        await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }
}
