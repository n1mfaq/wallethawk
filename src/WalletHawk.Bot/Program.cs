using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using WalletHawk.Bot;
using WalletHawk.Bot.Handlers;
using WalletHawk.Bot.Options;
using WalletHawk.Bot.Services;
using WalletHawk.Data;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Infrastructure;
using WalletHawk.Infrastructure.Telegram;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));

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

builder.Services.AddScoped<UpdateHandler>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<WalletService>();

builder.Services.AddSingleton<INotifier, TelegramNotifier>();
builder.Services.AddHostedService<BotHostedService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
