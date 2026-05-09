using Telegram.Bot;
using WalletHawk.Data;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Infrastructure;
using WalletHawk.Infrastructure.Telegram;
using WalletHawk.Worker;
using WalletHawk.Worker.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

builder.Services.AddWalletHawkData(connectionString);
builder.Services.AddWalletHawkInfrastructure(builder.Configuration);

builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var token = builder.Configuration["Bot:Token"]
        ?? throw new InvalidOperationException("Bot:Token is required");
    return new TelegramBotClient(token);
});

builder.Services.AddSingleton<INotifier, TelegramNotifier>();
builder.Services.AddHostedService<WalletPoller>();

await builder.Build().RunAsync();
