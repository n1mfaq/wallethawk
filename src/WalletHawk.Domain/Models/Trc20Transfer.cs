namespace WalletHawk.Domain.Models;

/// <summary>Normalised TRC20 transfer event from any explorer.</summary>
public sealed record Trc20Transfer(
    string TxHash,
    string FromAddress,
    string ToAddress,
    decimal Amount,
    string TokenSymbol,
    DateTimeOffset Timestamp);
