namespace WalletHawk.Worker.Options;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How often the worker polls explorer for new transactions.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>How many wallets to process per poll iteration.</summary>
    public int BatchSize { get; set; } = 50;
}
