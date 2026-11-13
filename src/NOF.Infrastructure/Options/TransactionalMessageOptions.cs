namespace NOF.Infrastructure;

public sealed class TransactionalMessageOptions
{
    public TransactionalMessageProcessorOptions Inbox { get; set; } = new();

    public TransactionalMessageProcessorOptions Outbox { get; set; } = new();
}

public sealed class TransactionalMessageProcessorOptions
{
    /// <summary>Gets or sets whether this enabled capability runs its processing and cleanup workers in this host.</summary>
    public bool RunWorkers { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 100;

    public int MaxRetryCount { get; set; } = 5;

    /// <summary>Gets or sets the minimum delay between outbox attempts; zero uses the polling cadence.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
}
