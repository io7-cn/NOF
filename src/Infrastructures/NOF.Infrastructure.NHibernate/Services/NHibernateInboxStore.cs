using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NHibernate.Linq;
using NOF.Application;

namespace NOF.Infrastructure.NHibernate;

/// <summary>Provider-backed inbox claims and receipt transitions, sharing the invocation unit of work.</summary>
public sealed partial class NHibernateInboxStore(global::NHibernate.ISession session, IUnitOfWork unitOfWork, IOptions<TransactionalMessageOptions> options,
    ILogger<NHibernateInboxStore> logger) : IInboxStore
{
    private readonly global::NHibernate.ISession _session = session;
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Inbox;
    private readonly ILogger<NHibernateInboxStore> _logger = logger;
    /// <inheritdoc />
    public async Task ReceiveAsync(NOFInboxMessage message, CancellationToken cancellationToken)
    {
        _session.Save(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid messageId, string route, CancellationToken cancellationToken)
        => _session.Query<NOFInboxMessage>().AnyAsync(message => message.Id == messageId && message.Route == route, cancellationToken);
    /// <inheritdoc />
    public async Task<NOFInboxMessage?> FindPendingAsync(NOFInboxMessage message, CancellationToken cancellationToken)
    {
        var receipt = await _session.Query<NOFInboxMessage>().FirstOrDefaultAsync(
            row => row.Id == message.Id && row.Route == message.Route && row.Status == InboxMessageStatus.Pending
                && row.ClaimedBy != null && row.ClaimedBy == message.ClaimedBy, cancellationToken);
        if (receipt is null)
        {
            return null;
        }
        // Bulk claims bypass the stateful session cache. Reload before capturing the optimistic-lock snapshot.
        _session.SetReadOnly(receipt, false);
        await _session.RefreshAsync(receipt, cancellationToken);
        return receipt.Status == InboxMessageStatus.Pending && receipt.ClaimedBy is not null && receipt.ClaimedBy == message.ClaimedBy ? receipt : null;
    }
    /// <inheritdoc />
    public async Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken)
    {
        var dbContext = _session;

        await dbContext.Query<NOFInboxMessage>()
            .Where(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy)
            .UpdateAsync(m => new NOFInboxMessage { ErrorMessage = error, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);

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

        await dbContext.Query<NOFInboxMessage>()
            .Where(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending && m.ClaimedBy != null && m.ClaimedBy == message.ClaimedBy)
            .UpdateAsync(m => new NOFInboxMessage { Status = InboxMessageStatus.Failed, ErrorMessage = error, FailedAtUtc = failedAt, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);

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

        await NHibernateMessageRecovery.MarkExpiredExhaustedInboxMessagesAsFailedAsync(
            dbContext,
            _options.MaxRetryCount,
            now,
            cancellationToken);

        var candidates = await _session.Query<NOFInboxMessage>()
            .Where(m => m.Status == InboxMessageStatus.Pending &&
                        m.RetryCount < _options.MaxRetryCount &&
                        (m.ClaimedBy == null || m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).Take(batchSize)
            .Select(m => new { m.Id, m.Route }).ToListAsync(cancellationToken);
        var claimedCount = 0;
        foreach (var m in candidates)
        {
            claimedCount += await _session.Query<NOFInboxMessage>()
                .Where(candidate => candidate.Id == m.Id && candidate.Route == m.Route)
                .Where(candidate => candidate.Status == InboxMessageStatus.Pending &&
                        candidate.RetryCount < _options.MaxRetryCount &&
                        (candidate.ClaimedBy == null || candidate.ClaimExpiresAtUtc == null || candidate.ClaimExpiresAtUtc <= now))
                .UpdateAsync(candidate => new NOFInboxMessage { RetryCount = candidate.RetryCount + 1, ClaimedBy = lockId, ClaimExpiresAtUtc = expiresAt }, cancellationToken);
        }
        if (claimedCount == 0)
        {
            yield break;
        }

        var claimed = await dbContext.Query<NOFInboxMessage>()
            .WithOptions(options => options.SetReadOnly(true))
            .Where(m => m.ClaimedBy == lockId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Select(row => new NOFInboxMessage
            {
                Id = row.Id,
                CreatedAtUtc = row.CreatedAtUtc,
                RetryCount = row.RetryCount,
                Route = row.Route,
                Payload = row.Payload,
                Headers = row.Headers,
                ProcessedAtUtc = row.ProcessedAtUtc,
                FailedAtUtc = row.FailedAtUtc,
                ErrorMessage = row.ErrorMessage,
                ClaimedBy = row.ClaimedBy,
                ClaimExpiresAtUtc = row.ClaimExpiresAtUtc,
                Status = row.Status
            })
            .ToListAsync(cancellationToken);

        foreach (var msgFromDb in claimed)
        {
            yield return msgFromDb;
        }
    }
}
