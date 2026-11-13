namespace NOF.Infrastructure;

public sealed partial class PersistenceInboxStore
{
    public async Task<(int Messages, int Orders)> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        var deletedCount = await _session.Set<NOFInboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Processed)
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < olderThan)
            .ExecuteDeleteAsync(cancellationToken);

        var deletedOrderStateCount = await _session.Set<NOFInboxOrderState>()
            .Where(state => state.CompletedAtUtc != null && state.CompletedAtUtc < olderThan)
            .Where(state => !_session.Set<NOFInboxMessage>().Any(message =>
                message.Route == state.Route &&
                message.OrderKey == state.OrderKey &&
                message.Status == InboxMessageStatus.Pending))
            .ExecuteDeleteAsync(cancellationToken);

        return (deletedCount, deletedOrderStateCount);
    }
}
