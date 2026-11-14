using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Application;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Provider-backed inbox claims and receipt transitions, sharing the invocation unit of work.</summary>
public sealed partial class EfCoreInboxStore(DbContext session, IUnitOfWork unitOfWork, IOptions<TransactionalMessageOptions> options,
    ILogger<EfCoreInboxStore> logger) : IInboxStore
{
    private readonly DbContext _session = session;
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Inbox;
    private readonly ILogger<EfCoreInboxStore> _logger = logger;
    /// <inheritdoc />
    public async Task ReceiveAsync(NOFInboxMessage message, CancellationToken cancellationToken)
    {
        _session.Set<NOFInboxMessage>().Add(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid messageId, string route, CancellationToken cancellationToken)
        => _session.Set<NOFInboxMessage>().AnyAsync(message => message.Id == messageId && message.Route == route, cancellationToken);
    /// <inheritdoc />
    public async Task<NOFInboxMessage?> FindPendingAsync(NOFInboxMessage message, CancellationToken cancellationToken)
    {
        var tracked = _session.ChangeTracker.Entries<NOFInboxMessage>()
            .FirstOrDefault(entry => entry.Entity.Id == message.Id && entry.Entity.Route == message.Route);
        if (tracked is not null)
        {
            // Native bulk claims bypass tracked values; reload a receipt already present in this scope.
            await tracked.ReloadAsync(cancellationToken);
            var row = tracked.Entity;
            return tracked.State != EntityState.Detached && row.Status == InboxMessageStatus.Pending
                && row.ClaimedBy is not null && row.ClaimedBy == message.ClaimedBy ? row : null;
        }
        return await _session.Set<NOFInboxMessage>().FirstOrDefaultAsync(
            row => row.Id == message.Id && row.Route == message.Route && row.Status == InboxMessageStatus.Pending
                && row.ClaimedBy != null && row.ClaimedBy == message.ClaimedBy, cancellationToken);
    }
    /// <inheritdoc />
    public async Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken)
    {
        var dbContext = _session;

        await dbContext.Set<NOFInboxMessage>()
            .Where(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.ErrorMessage, error)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);

        _logger.LogWarning(
            "Inbox message {InboxId} scheduled for retry #{RetryCount}. Error: {Error}",
            message.Id,
            message.RetryCount,
            error);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(
        NOFInboxMessage message,
        string error,
        CancellationToken cancellationToken,
        Exception? ex = null)
    {
        var dbContext = _session;
        var failedAt = DateTime.UtcNow;

        await dbContext.Set<NOFInboxMessage>()
            .Where(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, InboxMessageStatus.Failed)
                .SetProperty(m => m.ErrorMessage, error)
                .SetProperty(m => m.FailedAtUtc, failedAt)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);

        if (ex is null)
        {
            _logger.LogError(
                "Inbox message {InboxId} marked as permanently failed after {RetryCount} retries. Error: {Error}",
                message.Id,
                message.RetryCount,
                error);
        }
        else
        {
            _logger.LogError(
                ex,
                "Inbox message {InboxId} marked as permanently failed after {RetryCount} retries. Error: {Error}",
                message.Id,
                message.RetryCount,
                error);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NOFInboxMessage> AtomicClaimPendingMessagesAsync(
        int batchSize,
        TimeSpan claimTimeout,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var dbContext = _session;
        if (batchSize <= 0)
        {
            batchSize = _options.BatchSize;
        }

        var lockId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(claimTimeout);

        await EfCoreMessageRecovery.MarkExpiredExhaustedInboxMessagesAsFailedAsync(
            dbContext,
            _options.MaxRetryCount,
            now,
            cancellationToken);

        var claimedCount = await dbContext.Set<NOFInboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Pending &&
                        m.RetryCount < _options.MaxRetryCount &&
                        (m.ClaimedBy == null || m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(batchSize)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                    .SetProperty(m => m.ClaimedBy, lockId)
                    .SetProperty(m => m.ClaimExpiresAtUtc, expiresAt), cancellationToken);
        if (claimedCount == 0)
        {
            yield break;
        }

        var claimed = await dbContext.Set<NOFInboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimedBy == lockId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var msgFromDb in claimed)
        {
            yield return msgFromDb;
        }
    }
}
