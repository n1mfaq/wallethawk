using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletHawk.Data;
using WalletHawk.Domain.Abstractions;
using WalletHawk.Domain.Entities;
using WalletHawk.Domain.Models;
using WalletHawk.Domain.Services;
using WalletHawk.Worker.Options;

namespace WalletHawk.Worker;

public sealed class WalletPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _opt;
    private readonly ILogger<WalletPoller> _log;

    public WalletPoller(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> opt,
        ILogger<WalletPoller> log)
    {
        _scopeFactory = scopeFactory;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("WalletHawk worker started, polling every {Seconds}s", _opt.PollSeconds);

        var delay = TimeSpan.FromSeconds(Math.Max(5, _opt.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Polling iteration failed");
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var explorer = scope.ServiceProvider.GetRequiredService<ITronExplorerClient>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

        // Take batch oldest-checked-first to spread load evenly
        var wallets = await db.Wallets
            .Include(w => w.User)
            .Where(w => w.Network == WalletNetwork.TronTrc20)
            .OrderBy(w => w.LastCheckedAt ?? DateTimeOffset.MinValue)
            .Take(_opt.BatchSize)
            .ToListAsync(ct);

        if (wallets.Count == 0) return;

        foreach (var w in wallets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await CheckWalletAsync(db, explorer, notifier, w, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to process wallet {Id} ({Address})", w.Id, w.Address);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task CheckWalletAsync(
        AppDbContext db,
        ITronExplorerClient explorer,
        INotifier notifier,
        Wallet wallet,
        CancellationToken ct)
    {
        var transfers = await explorer.GetUsdtTransfersAsync(wallet.Address, limit: 20, ct);
        wallet.LastCheckedAt = DateTimeOffset.UtcNow;

        if (transfers.Count == 0) return;

        // Newest first (TronGrid returns desc by default).
        // On first ever check we just record the latest hash and skip notifications.
        if (string.IsNullOrEmpty(wallet.LastTxHash))
        {
            wallet.LastTxHash = transfers[0].TxHash;
            return;
        }

        // Collect new transfers (everything before the previously seen hash).
        var newOnes = new List<Trc20Transfer>();
        foreach (var tx in transfers)
        {
            if (tx.TxHash == wallet.LastTxHash) break;
            newOnes.Add(tx);
        }
        if (newOnes.Count == 0) return;

        // Update marker first (in case notification fails we don't spam later).
        wallet.LastTxHash = transfers[0].TxHash;

        // Send oldest-first so user reads timeline naturally.
        newOnes.Reverse();
        foreach (var tx in newOnes)
        {
            var isIn = string.Equals(tx.ToAddress, wallet.Address, StringComparison.OrdinalIgnoreCase);
            var arrow = isIn ? "📥 IN" : "📤 OUT";
            var counterparty = isIn ? tx.FromAddress : tx.ToAddress;

            // Persist the transaction (idempotent via unique (WalletId, TxHash) index).
            db.Transactions.Add(new Transaction
            {
                WalletId = wallet.Id,
                TxHash = tx.TxHash,
                Direction = isIn ? TxDirection.In : TxDirection.Out,
                Amount = tx.Amount,
                TokenSymbol = tx.TokenSymbol,
                Counterparty = counterparty,
                BlockTime = tx.Timestamp,
            });

            var label = string.IsNullOrEmpty(wallet.Label)
                ? TronAddress.Mask(wallet.Address)
                : wallet.Label;

            var text =
                $"{arrow}  *{Esc(tx.Amount.ToString("0.######"))} {Esc(tx.TokenSymbol)}*\n" +
                $"wallet: `{Esc(label)}`\n" +
                $"{(isIn ? "from" : "to")}: `{Esc(TronAddress.Mask(counterparty))}`\n" +
                $"[tronscan](https://tronscan.org/#/transaction/{tx.TxHash})";

            try
            {
                await notifier.NotifyAsync(wallet.User.TelegramUserId, text, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Notify failed for user {UserId}", wallet.User.TelegramUserId);
            }
        }
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
    }
}
