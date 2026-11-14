using Microsoft.EntityFrameworkCore;
namespace NOF.Infrastructure.EntityFrameworkCore;

public sealed partial class EfCoreOutboxStore
{
    /// <inheritdoc />
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => _session.Set<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Sent)
            .Where(m => m.SentAtUtc != null && m.SentAtUtc < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
}
