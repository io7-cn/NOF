using Microsoft.EntityFrameworkCore;
namespace NOF.Infrastructure.EntityFrameworkCore;

public sealed partial class EfCoreInboxStore
{
    /// <inheritdoc />
    public Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
        => _session.Set<NOFInboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Processed)
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
}
