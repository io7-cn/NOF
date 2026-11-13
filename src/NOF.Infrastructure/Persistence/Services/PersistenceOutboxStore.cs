using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Provider-backed outbox storage, isolated from dispatch and application execution.</summary>
public sealed partial class PersistenceOutboxStore(IPersistenceSession session, IOptions<TransactionalMessageOptions> options,
    ILogger<PersistenceOutboxStore> logger) : IOutboxStore
{
    private readonly IPersistenceSession _session = session;
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Outbox;
    private readonly ILogger<PersistenceOutboxStore> _logger = logger;
    /// <inheritdoc />
    public void Stage(NOFOutboxMessage message) => _session.Set<NOFOutboxMessage>().Add(message);
    public async IAsyncEnumerable<NOFOutboxMessage> AtomicClaimPendingMessagesAsync(
        int batchSize = 100,
        TimeSpan? claimTimeout = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            batchSize = _options.BatchSize;
        }

        var timeout = claimTimeout ?? _options.ClaimTimeout;
        var lockId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(timeout);

        await TransactionalMessageRecovery.MarkExpiredExhaustedOutboxMessagesAsFailedAsync(
            _session,
            _options.MaxRetryCount,
            now,
            cancellationToken);

        var rowsUpdated = await _session.Set<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Pending &&
                        m.RetryCount < _options.MaxRetryCount &&
                        (m.ClaimedBy == null || m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(batchSize)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                    .SetProperty(m => m.ClaimedBy, lockId)
                    .SetProperty(m => m.ClaimExpiresAtUtc, expiresAt),
                cancellationToken);

        if (rowsUpdated == 0)
        {
            yield break;
        }

        var claimedMessages = await _session.Set<NOFOutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimedBy == lockId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var msgFromDb in claimedMessages)
        {
            yield return msgFromDb;
        }
    }

    public async ValueTask AtomicReleaseClaimsAsync(
        IEnumerable<Guid> messageIds,
        CancellationToken cancellationToken = default)
    {
        await _session.Set<NOFOutboxMessage>()
            .Where(m => messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.RetryCount, m => m.RetryCount - 1)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);
    }

    public async ValueTask<int> AtomicMarkAsSentAsync(
        IEnumerable<Guid> messageIds,
        CancellationToken cancellationToken = default)
    {
        var sentAt = DateTime.UtcNow;

        return await _session.Set<NOFOutboxMessage>()
            .Where(m => messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Sent)
                .SetProperty(m => m.SentAtUtc, sentAt)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);
    }

    public async ValueTask AtomicRecordDeliveryFailureAsync(
        NOFOutboxMessage message,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var failedAt = DateTime.UtcNow;
        var rowsUpdated = message.RetryCount >= _options.MaxRetryCount
            ? await _session.Set<NOFOutboxMessage>()
                .Where(m => m.Id == message.Id && m.Status == OutboxMessageStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                    .SetProperty(m => m.ErrorMessage, errorMessage)
                    .SetProperty(m => m.FailedAtUtc, failedAt)
                    .SetProperty(m => m.ClaimedBy, (string?)null)
                    .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                    cancellationToken)
            : await _session.Set<NOFOutboxMessage>()
                .Where(m => m.Id == message.Id && m.Status == OutboxMessageStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                    .SetProperty(m => m.ErrorMessage, errorMessage)
                    .SetProperty(m => m.FailedAtUtc, failedAt)
                    .SetProperty(m => m.ClaimedBy, (string?)null)
                    .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                    cancellationToken);

        if (rowsUpdated == 0)
        {
            _logger.LogDebug("Message {MessageId} already processed or not in pending state", message.Id);
            return;
        }

        if (message.RetryCount >= _options.MaxRetryCount)
        {
            _logger.LogWarning(
                "Message {MessageId} marked as permanently failed after {RetryCount} retries. Error: {Error}",
                message.Id, message.RetryCount, errorMessage);
        }
        else
        {
            _logger.LogWarning(
                "Message {MessageId} scheduled for retry #{RetryCount}. Error: {Error}",
                message.Id, message.RetryCount, errorMessage);
        }
    }
}
