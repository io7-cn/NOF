namespace NOF.Infrastructure;

public sealed partial class PersistenceOutboxStore
{
    /// <inheritdoc />
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => _session.Set<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Sent)
            .Where(m => m.SentAtUtc != null && m.SentAtUtc < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
}
