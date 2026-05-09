using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using WalletHawk.Bot.Handlers;
using WalletHawk.Bot.Options;

namespace WalletHawk.Bot;

public sealed class BotHostedService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotHostedService> _log;

    public BotHostedService(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> _, // ensure DI exists
        ILogger<BotHostedService> log)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _bot.GetMe(stoppingToken);
        _log.LogInformation("WalletHawk bot started as @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            DropPendingUpdates = true,
        };

        await _bot.ReceiveAsync(
            updateHandler: new ScopedUpdateHandlerAdapter(_scopeFactory),
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
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
