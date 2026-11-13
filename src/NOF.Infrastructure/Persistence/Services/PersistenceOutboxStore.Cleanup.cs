namespace NOF.Infrastructure;

public sealed partial class PersistenceOutboxStore
{
    public async Task<(int Messages, int Compacted, int Completed)> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        var deletedCount = await _session.Set<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Sent)
            .Where(m => m.SentAtUtc != null && m.SentAtUtc < olderThan)
            .ExecuteDeleteAsync(cancellationToken);

        var compactedOrderStateCount = await _session.Set<NOFOutboxOrderState>()
            .Where(state => state.CreatedAtUtc < olderThan)
            .Where(state => _session.Set<NOFOutboxOrderState>().Any(newer =>
                newer.OrderKey == state.OrderKey && newer.Sequence > state.Sequence))
            .ExecuteDeleteAsync(cancellationToken);

        var deletedCompletedOrderStateCount = await _session.Set<NOFOutboxOrderState>()
            .Where(state => state.CompletesOrderKey && state.CreatedAtUtc < olderThan)
            .Where(state => !_session.Set<NOFOutboxMessage>().Any(message =>
                message.OrderKey == state.OrderKey && message.Status != OutboxMessageStatus.Sent))
            .ExecuteDeleteAsync(cancellationToken);

        return (deletedCount, compactedOrderStateCount, deletedCompletedOrderStateCount);
    }
}
