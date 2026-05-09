using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WalletHawk.Bot.Options;
using WalletHawk.Bot.Payments;
using WalletHawk.Bot.Services;
using WalletHawk.Domain.Services;
using DomainUser = WalletHawk.Domain.Entities.User;
using WalletNetwork = WalletHawk.Domain.Entities.WalletNetwork;

namespace WalletHawk.Bot.Handlers;

public sealed class UpdateHandler : IUpdateHandler
{
    private readonly UserService _users;
    private readonly WalletService _wallets;
    private readonly PaymentService _payments;
    private readonly AdminService _admin;
    private readonly BotOptions _opt;
    private readonly ILogger<UpdateHandler> _log;

    public UpdateHandler(
        UserService users,
        WalletService wallets,
        PaymentService payments,
        AdminService admin,
        IOptions<BotOptions> opt,
        ILogger<UpdateHandler> log)
    {
        _users = users;
        _wallets = wallets;
        _payments = payments;
        _admin = admin;
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
                case "/dashboard":
                case "/app": await CmdDashboard(bot, msg, ct); break;

                // ── admin only ──
                case "/admin": await CmdAdmin(bot, msg, ct); break;
                case "/stats": await CmdAdminStats(bot, msg, ct); break;
                case "/grant_pro": await CmdGrantPro(bot, msg, args, ct); break;
                case "/revoke_pro": await CmdRevokePro(bot, msg, args, ct); break;
                case "/user": await CmdAdminUser(bot, msg, args, ct); break;
                case "/wallets": await CmdAdminWallets(bot, msg, args, ct); break;
                case "/broadcast": await CmdBroadcast(bot, msg, args, ct); break;

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
            "`/dashboard` — open Mini App\n" +
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

    private async Task CmdDashboard(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opt.WebAppUrl))
        {
            await bot.SendMessage(msg.Chat.Id, "Dashboard is not configured yet.", cancellationToken: ct);
            return;
        }

        var keyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
            Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithWebApp(
                "🦅 Open dashboard",
                new Telegram.Bot.Types.WebAppInfo(_opt.WebAppUrl)));

        await bot.SendMessage(msg.Chat.Id,
            "Tap below to open your WalletHawk dashboard inside Telegram:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task CmdUpgrade(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        try
        {
            var invoice = await _payments.CreateProInvoiceAsync(msg.From!.Id, ct);

            var text =
                "*WalletHawk Pro*\n\n" +
                "• unlimited wallets\n" +
                "• instant alerts \\(every 30s\\)\n" +
                "• priority support\n\n" +
                $"Pay *4\\.99 USDT* and Pro activates automatically:\n[👉 pay]({invoice.PayUrl})";

            await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.MarkdownV2,
                linkPreviewOptions: new Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CryptoBot invoice creation failed");
            // Fallback: show contact
            var fallback =
                "*WalletHawk Pro* — $4\\.99/mo\n\n" +
                "• unlimited wallets\n" +
                "• instant alerts\n\n" +
                $"DM @{_opt.OwnerUsername} to activate manually\\.";
            await bot.SendMessage(msg.Chat.Id, fallback, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Admin commands  —  visible only when msg.From.Username == BotOwner
    // ──────────────────────────────────────────────────────────────────────

    private bool RequireAdmin(Message msg) => msg.From is { } u && _admin.IsAdmin(u.Id);

    private static string EscHtml(string? s) =>
        string.IsNullOrEmpty(s) ? "" :
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private async Task CmdAdmin(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        if (!RequireAdmin(msg)) { await CmdHelp(bot, msg, ct); return; }

        var help =
            "🔑 <b>Admin commands</b>\n" +
            "<pre>" +
            "/stats                       usage counters\n" +
            "/user @name | id             user info\n" +
            "/wallets @name | id          user's wallets\n" +
            "/grant_pro @name [days=30]   grant Pro\n" +
            "/revoke_pro @name            remove Pro\n" +
            "/broadcast &lt;message&gt;         send to ALL users" +
            "</pre>";
        await bot.SendMessage(msg.Chat.Id, help, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task CmdAdminStats(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        if (!RequireAdmin(msg))
        {
            // Non-admins: redirect to /me-style summary
            var me = await _users.GetOrCreateAsync(msg.From!.Id, msg.From.Username, msg.From.FirstName, ct);
            await CmdMe(bot, msg, me, ct);
            return;
        }

        var s = await _admin.GetStatsAsync(ct);
        var mrr = (s.Pro * 4.99m).ToString("0.00");

        var text =
            "📊 <b>WalletHawk stats</b>\n" +
            "<pre>" +
            $"users     {s.Users}  (+{s.NewUsers24h} in 24h)\n" +
            $"pro       {s.Pro}\n" +
            $"wallets   {s.Wallets}  (+{s.NewWallets24h} in 24h)\n" +
            $"mrr       ${mrr}" +
            "</pre>";
        await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task CmdGrantPro(ITelegramBotClient bot, Message msg, string args, CancellationToken ct)
    {
        if (!RequireAdmin(msg)) return;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(msg.Chat.Id, "Usage: /grant_pro <@username|tg_id> [days=30]", cancellationToken: ct);
            return;
        }

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var who = parts[0];
        var days = parts.Length > 1 && int.TryParse(parts[1], out var d) && d > 0 ? d : 30;

        var user = await _admin.GrantProAsync(who, days, ct);
        if (user is null)
        {
            await bot.SendMessage(msg.Chat.Id, $"User '{who}' not found.", cancellationToken: ct);
            return;
        }

        await bot.SendMessage(msg.Chat.Id,
            $"✅ Granted Pro to @{user.Username ?? user.TelegramUserId.ToString()} until {user.ProUntil:yyyy-MM-dd}",
            cancellationToken: ct);

        // Also notify the user themselves
        try
        {
            await bot.SendMessage(user.TelegramUserId,
                $"🎁 <b>WalletHawk Pro</b> has been granted to you for <b>{days} days</b>.\nEnjoy unlimited wallets 🦅",
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to notify user {Id} about granted Pro", user.TelegramUserId);
        }
    }

    private async Task CmdRevokePro(ITelegramBotClient bot, Message msg, string args, CancellationToken ct)
    {
        if (!RequireAdmin(msg)) return;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(msg.Chat.Id, "Usage: /revoke_pro <@username|tg_id>", cancellationToken: ct);
            return;
        }

        var user = await _admin.RevokeProAsync(args.Trim(), ct);
        if (user is null)
        {
            await bot.SendMessage(msg.Chat.Id, $"User '{args}' not found.", cancellationToken: ct);
            return;
        }

        await bot.SendMessage(msg.Chat.Id,
            $"🚫 Revoked Pro from @{user.Username ?? user.TelegramUserId.ToString()}",
            cancellationToken: ct);
    }

    private async Task CmdAdminUser(ITelegramBotClient bot, Message msg, string args, CancellationToken ct)
    {
        if (!RequireAdmin(msg)) return;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(msg.Chat.Id, "Usage: /user <@username|tg_id>", cancellationToken: ct);
            return;
        }

        var u = await _admin.FindUserAsync(args.Trim(), ct);
        if (u is null)
        {
            await bot.SendMessage(msg.Chat.Id, $"User '{args}' not found.", cancellationToken: ct);
            return;
        }

        var walletCount = await _wallets.CountAsync(u.Id, ct);
        var plan = u.IsPro ? $"Pro until {u.ProUntil:yyyy-MM-dd}" : "Free";

        var text =
            $"👤 <b>User #{u.Id}</b>\n" +
            "<pre>" +
            $"tg id     {u.TelegramUserId}\n" +
            $"username  @{EscHtml(u.Username) ?? "—"}\n" +
            $"name      {EscHtml(u.FirstName) ?? "—"}\n" +
            $"plan      {EscHtml(plan)}\n" +
            $"wallets   {walletCount}\n" +
            $"joined    {u.CreatedAt:yyyy-MM-dd}" +
            "</pre>";
        await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task CmdAdminWallets(ITelegramBotClient bot, Message msg, string args, CancellationToken ct)
    {
        if (!RequireAdmin(msg)) return;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(msg.Chat.Id, "Usage: /wallets <@username|tg_id>", cancellationToken: ct);
            return;
        }

        var u = await _admin.FindUserAsync(args.Trim(), ct);
        if (u is null)
        {
            await bot.SendMessage(msg.Chat.Id, $"User '{args}' not found.", cancellationToken: ct);
            return;
        }

        var ws = await _admin.GetWalletsAsync(u.Id, ct);
        if (ws.Count == 0)
        {
            await bot.SendMessage(msg.Chat.Id, $"@{u.Username ?? u.TelegramUserId.ToString()} has no wallets.", cancellationToken: ct);
            return;
        }

        var handle = u.Username ?? u.TelegramUserId.ToString();
        var lines = ws.Select(w =>
        {
            var label = string.IsNullOrEmpty(w.Label) ? "" : $"  —  {EscHtml(w.Label)}";
            return $"#{w.Id,-3} {EscHtml(TronAddress.Mask(w.Address))}{label}";
        });
        var text =
            $"👛 <b>Wallets of @{EscHtml(handle)}</b>\n" +
            "<pre>" + string.Join('\n', lines) + "</pre>";
        await bot.SendMessage(msg.Chat.Id, text, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task CmdBroadcast(ITelegramBotClient bot, Message msg, string args, CancellationToken ct)
    {
        if (!RequireAdmin(msg)) return;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(msg.Chat.Id, "Usage: /broadcast <message>", cancellationToken: ct);
            return;
        }

        var ids = await _admin.GetAllTelegramIdsAsync(ct);
        var ok = 0;
        var failed = 0;

        foreach (var id in ids)
        {
            try
            {
                await bot.SendMessage(id, args, cancellationToken: ct);
                ok++;
                // Telegram rate limit: max ~30 msg/s overall, keep it slow.
                await Task.Delay(50, ct);
            }
            catch (Exception ex)
            {
                failed++;
                _log.LogWarning(ex, "Broadcast failed for user {Id}", id);
            }
        }

        await bot.SendMessage(msg.Chat.Id,
            $"📢 Broadcast done: {ok} delivered, {failed} failed (out of {ids.Count}).",
            cancellationToken: ct);
    }

    /// <summary>Escape MarkdownV2 reserved chars.</summary>
    private static string Esc(string s)
    {
        ReadOnlySpan<char> reserved = "_*[]()~`>#+-=|{}.!";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (reserved.IndexOf(c) >= 0) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }}