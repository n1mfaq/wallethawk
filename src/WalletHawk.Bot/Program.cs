using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using WalletHawk.Bot;
using WalletHawk.Bot.Handlers;
using WalletHawk.Bot.Options;
using WalletHawk.Bot.Payments;
using WalletHawk.Bot.Services;
using WalletHawk.Data;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Infrastructure;
using WalletHawk.Infrastructure.Telegram;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<PaymentService>();

builder.Services.AddSingleton<INotifier, TelegramNotifier>();
builder.Services.AddHostedService<BotHostedService>();

builder.Services.AddHealthChecks();

builder.Services.AddCors(options =>
{
    options.AddPolicy("PublicReadOnly", policy =>
    {
        policy.AllowAnyOrigin()
              .WithMethods("GET")
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("PublicReadOnly");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Text("WalletHawk bot is alive 🦅"));
app.MapPost("/webhooks/cryptobot", CryptoBotWebhook.HandleAsync);

app.MapGet("/stats", async (AppDbContext db, CancellationToken ct) =>
{
    var users = await db.Users.CountAsync(ct);
    var wallets = await db.Wallets.CountAsync(ct);
    var pro = await db.Users.CountAsync(u => u.IsPro, ct);
    return Results.Json(new
    {
        users,
        wallets,
        pro,
        updatedAt = DateTimeOffset.UtcNow,
    });
}).WithName("PublicStats");

await app.RunAsync();
