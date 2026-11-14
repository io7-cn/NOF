using NHibernate.Linq;
namespace NOF.Infrastructure.NHibernate;

public sealed partial class NHibernateInboxStore
{
    /// <inheritdoc />
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => _session.Query<NOFInboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Processed)
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < olderThan)
            .DeleteAsync(cancellationToken);
}
