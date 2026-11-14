namespace NOF.Infrastructure;

public sealed partial class PersistenceInboxStore
{
    /// <inheritdoc />
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => _session.Set<NOFInboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Processed)
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
}
