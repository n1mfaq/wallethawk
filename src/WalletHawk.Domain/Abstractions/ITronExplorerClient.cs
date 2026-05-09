using WalletHawk.Domain.Models;

namespace WalletHawk.Domain.Abstractions;

public interface ITronExplorerClient
{
    /// <summary>Get latest TRC20-USDT transfers for the given address (newest first).</summary>
    Task<IReadOnlyList<Trc20Transfer>> GetUsdtTransfersAsync(
        string address,
        int limit = 20,
        CancellationToken ct = default);
}
