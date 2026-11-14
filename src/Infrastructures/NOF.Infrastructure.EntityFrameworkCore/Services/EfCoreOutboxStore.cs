using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Application;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Provider-backed outbox storage, isolated from dispatch and application execution.</summary>
public sealed partial class EfCoreOutboxStore(DbContext session, IOptions<TransactionalMessageOptions> options,
    ILogger<EfCoreOutboxStore> logger) : IOutboxStore
{
    private readonly DbContext _session = session;
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Outbox;
    private readonly ILogger<EfCoreOutboxStore> _logger = logger;
    /// <inheritdoc />
    public void Stage(NOFOutboxMessage message) => _session.Set<NOFOutboxMessage>().Add(message);
    /// <inheritdoc />
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

        await EfCoreMessageRecovery.MarkExpiredExhaustedOutboxMessagesAsFailedAsync(
            _session,
            _options.MaxRetryCount,
            now,
            cancellationToken);

        var rowsUpdated = await _session.Set<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Pending &&
                        m.RetryCount < _options.MaxRetryCount &&
                        (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now) &&
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

    /// <inheritdoc />
    public async ValueTask AtomicReleaseClaimsAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            await _session.Set<NOFOutboxMessage>()
                .Where(m => m.Id == message.Id && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy && m.Status == OutboxMessageStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.RetryCount, m => m.RetryCount - 1)
                    .SetProperty(m => m.ClaimedBy, (string?)null)
                    .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> AtomicMarkAsSentAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var sentAt = DateTime.UtcNow;
        foreach (var message in messages)
        {
            count += await _session.Set<NOFOutboxMessage>()
                .Where(m => m.Id == message.Id && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy && m.Status == OutboxMessageStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, OutboxMessageStatus.Sent)
                    .SetProperty(m => m.SentAtUtc, sentAt)
                    .SetProperty(m => m.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(m => m.ClaimedBy, (string?)null)
                    .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null), cancellationToken);
        }
        return count;
    }

    /// <inheritdoc />
    public async ValueTask AtomicRecordDeliveryFailureAsync(NOFOutboxMessage message, string errorMessage,
        bool permanent = false, CancellationToken cancellationToken = default)
    {
        var failedAt = DateTime.UtcNow;
        var terminal = permanent || message.RetryCount >= _options.MaxRetryCount;
        var status = terminal ? OutboxMessageStatus.Failed : OutboxMessageStatus.Pending;
        DateTime? nextAttempt = terminal ? null : failedAt.Add(_options.RetryDelay);
        var error = errorMessage.Length > 2048 ? errorMessage[..2048] : errorMessage;
        var updated = await _session.Set<NOFOutboxMessage>()
            .Where(m => m.Id == message.Id && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy && m.Status == OutboxMessageStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, status)
                .SetProperty(m => m.ErrorMessage, error)
                .SetProperty(m => m.FailedAtUtc, failedAt)
                .SetProperty(m => m.NextAttemptAtUtc, nextAttempt)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null), cancellationToken);
        if (updated == 0)
        {
            _logger.LogDebug("Ignored failure from expired owner for outbox message {MessageId}", message.Id);
        }
    }
}
