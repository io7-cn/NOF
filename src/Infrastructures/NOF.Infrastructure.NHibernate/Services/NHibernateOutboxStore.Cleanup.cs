using NHibernate.Linq;
namespace NOF.Infrastructure.NHibernate;

public sealed partial class NHibernateOutboxStore
{
    /// <inheritdoc />
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => _session.Query<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Sent)
            .Where(m => m.SentAtUtc != null && m.SentAtUtc < olderThan)
            .DeleteAsync(cancellationToken);
}
