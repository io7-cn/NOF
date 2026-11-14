using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NHibernate.Linq;
using NOF.Application;

namespace NOF.Infrastructure.NHibernate;

/// <summary>Provider-backed outbox storage, isolated from dispatch and application execution.</summary>
public sealed partial class NHibernateOutboxStore(global::NHibernate.ISession session, IOptions<TransactionalMessageOptions> options,
    ILogger<NHibernateOutboxStore> logger) : IOutboxStore
{
    private readonly global::NHibernate.ISession _session = session;
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Outbox;
    private readonly ILogger<NHibernateOutboxStore> _logger = logger;
    /// <inheritdoc />
    public void Stage(NOFOutboxMessage message) => _session.Save(message);
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

        await NHibernateMessageRecovery.MarkExpiredExhaustedOutboxMessagesAsFailedAsync(
            _session,
            _options.MaxRetryCount,
            now,
            cancellationToken);

        var candidates = await _session.Query<NOFOutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Pending &&
                        m.RetryCount < _options.MaxRetryCount &&
                        (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now) &&
                        (m.ClaimedBy == null || m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).Take(batchSize)
            .Select(m => new { m.Id }).ToListAsync(cancellationToken);
        var rowsUpdated = 0;
        foreach (var m in candidates)
        {
            rowsUpdated += await _session.Query<NOFOutboxMessage>()
                .Where(candidate => candidate.Id == m.Id)
                .Where(candidate => candidate.Status == OutboxMessageStatus.Pending &&
                        candidate.RetryCount < _options.MaxRetryCount &&
                        (candidate.NextAttemptAtUtc == null || candidate.NextAttemptAtUtc <= now) &&
                        (candidate.ClaimedBy == null || candidate.ClaimExpiresAtUtc == null || candidate.ClaimExpiresAtUtc <= now))
                .UpdateAsync(candidate => new NOFOutboxMessage { RetryCount = candidate.RetryCount + 1, ClaimedBy = lockId, ClaimExpiresAtUtc = expiresAt }, cancellationToken);
        }

        if (rowsUpdated == 0)
        {
            yield break;
        }

        var claimedMessages = await _session.Query<NOFOutboxMessage>()
            .WithOptions(options => options.SetReadOnly(true))
            .Where(m => m.ClaimedBy == lockId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Select(row => new NOFOutboxMessage
            {
                Id = row.Id,
                CreatedAtUtc = row.CreatedAtUtc,
                RetryCount = row.RetryCount,
                MessageType = row.MessageType,
                SchemaVersion = row.SchemaVersion,
                NextAttemptAtUtc = row.NextAttemptAtUtc,
                Payload = row.Payload,
                Headers = row.Headers,
                SentAtUtc = row.SentAtUtc,
                FailedAtUtc = row.FailedAtUtc,
                ErrorMessage = row.ErrorMessage,
                ClaimedBy = row.ClaimedBy,
                ClaimExpiresAtUtc = row.ClaimExpiresAtUtc,
                Status = row.Status,
                TraceParent = row.TraceParent
            })
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
            await _session.Query<NOFOutboxMessage>()
                .Where(m => m.Id == message.Id && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy && m.Status == OutboxMessageStatus.Pending)
                .UpdateAsync(m => new NOFOutboxMessage { RetryCount = m.RetryCount - 1, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> AtomicMarkAsSentAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var sentAt = DateTime.UtcNow;
        foreach (var message in messages)
        {
            count += await _session.Query<NOFOutboxMessage>()
                .Where(m => m.Id == message.Id && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy && m.Status == OutboxMessageStatus.Pending)
                .UpdateAsync(m => new NOFOutboxMessage { Status = OutboxMessageStatus.Sent, SentAtUtc = sentAt, NextAttemptAtUtc = (DateTime?)null, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);
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
        var updated = await _session.Query<NOFOutboxMessage>()
            .Where(m => m.Id == message.Id && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy && m.Status == OutboxMessageStatus.Pending)
            .UpdateAsync(m => new NOFOutboxMessage { Status = status, ErrorMessage = error, FailedAtUtc = failedAt, NextAttemptAtUtc = nextAttempt, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);
        if (updated == 0)
        {
            _logger.LogDebug("Ignored failure from expired owner for outbox message {MessageId}", message.Id);
        }
    }
}
