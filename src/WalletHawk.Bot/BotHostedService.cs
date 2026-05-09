using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using WalletHawk.Bot.Handlers;
using WalletHawk.Bot.Options;

namespace WalletHawk.Bot;

public sealed class BotHostedService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotOptions _opt;
    private readonly ILogger<BotHostedService> _log;

    public BotHostedService(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> opt,
        ILogger<BotHostedService> log)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _bot.GetMe(stoppingToken);
        _log.LogInformation("WalletHawk bot started as @{Username}", me.Username);

        // One-shot setup of bot menu button + slash-command list.
        await ConfigureBotProfileAsync(stoppingToken);

        var receiverOptions = new ReceiverOptions
        {
            DropPendingUpdates = true,
        };

        await _bot.ReceiveAsync(
            updateHandler: new ScopedUpdateHandlerAdapter(_scopeFactory),
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }

    private async Task ConfigureBotProfileAsync(CancellationToken ct)
    {
        try
        {
            await _bot.SetMyCommands(new[]
            {
                new BotCommand { Command = "start",     Description = "welcome + help" },
                new BotCommand { Command = "add",       Description = "track a TRC20 wallet" },
                new BotCommand { Command = "list",      Description = "show tracked wallets" },
                new BotCommand { Command = "remove",    Description = "stop tracking by id" },
                new BotCommand { Command = "me",        Description = "your plan & counters" },
                new BotCommand { Command = "dashboard", Description = "open Mini App" },
                new BotCommand { Command = "upgrade",   Description = "go Pro" },
                new BotCommand { Command = "help",      Description = "show help" },
            }, cancellationToken: ct);

            if (!string.IsNullOrEmpty(_opt.WebAppUrl))
            {
                await _bot.SetChatMenuButton(
                    menuButton: new MenuButtonWebApp
                    {
                        Text = "🦅 dashboard",
                        WebApp = new WebAppInfo { Url = _opt.WebAppUrl },
                    },
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: bot still works without menu button / command list.
            _log.LogWarning(ex, "Failed to configure bot profile (commands/menu button)");
        }
    }

    /// <summary>Adapter that creates a DI scope per update.</summary>
    private sealed class ScopedUpdateHandlerAdapter : IUpdateHandler
    {
        private readonly IServiceScopeFactory _scopeFactory;
        public ScopedUpdateHandlerAdapter(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Telegram.Bot.Types.Update update, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
            await inner.HandleUpdateAsync(botClient, update, ct);
        }

        public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
            return inner.HandleErrorAsync(botClient, exception, source, ct);
        }
    }
}
