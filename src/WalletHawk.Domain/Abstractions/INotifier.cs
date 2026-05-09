namespace WalletHawk.Domain.Abstractions;

public interface INotifier
{
    Task NotifyAsync(long telegramUserId, string markdown, CancellationToken ct = default);
}
