using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Provider-backed inbox claims and ordering transitions, sharing the invocation unit of work.</summary>
public sealed partial class PersistenceInboxStore(IPersistenceSession session, IOptions<TransactionalMessageOptions> options,
    ILogger<PersistenceInboxStore> logger) : IInboxStore
{
    private readonly IPersistenceSession _session = session;
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Inbox;
    private readonly ILogger<PersistenceInboxStore> _logger = logger;
    public async Task ReceiveAsync(NOFInboxMessage message, InboxMessageOrder? order, CancellationToken cancellationToken)
    {
        await InboxMessageOrdering.EnsureStateAsync(_session, message.Id, message.Route, order, cancellationToken);
        _session.Set<NOFInboxMessage>().Add(message);
        await _session.SaveChangesAsync(cancellationToken);
    }
    public Task<bool> ExistsAsync(Guid messageId, string route, CancellationToken cancellationToken)
        => _session.Set<NOFInboxMessage>().AnyAsync(message => message.Id == messageId && message.Route == route, cancellationToken);
    public Task<NOFInboxMessage?> FindPendingAsync(NOFInboxMessage message, CancellationToken cancellationToken)
        => _session.Set<NOFInboxMessage>().FirstOrDefaultAsync(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending, cancellationToken);
    public async Task<NOFInboxOrderState?> LoadAndValidateOrderStateAsync(
        NOFInboxMessage message,
        CancellationToken cancellationToken)
    {
        var dbContext = _session;
        if (message.OrderKey is null && message.Sequence is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(message.OrderKey) || message.Sequence is null or <= 0)
        {
            throw new InvalidOperationException(
                $"Inbox message '{message.Id}' must have both a non-empty order key and a positive sequence, or neither.");
        }

        var orderState = await dbContext.Set<NOFInboxOrderState>()
            .FirstOrDefaultAsync(
                state => state.Route == message.Route && state.OrderKey == message.OrderKey,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Inbox order state for route '{message.Route}' and key '{message.OrderKey}' does not exist.");

        if (orderState.CompletedAtUtc is not null)
        {
            throw new InvalidOperationException($"Inbox order key '{message.OrderKey}' has already been completed.");
        }

        if (orderState.BlockedSequence is not null)
        {
            throw new InvalidOperationException(
                $"Inbox order key '{message.OrderKey}' is blocked at sequence {orderState.BlockedSequence}.");
        }

        if (!string.Equals(orderState.ClaimedBy, message.ClaimedBy, StringComparison.Ordinal) ||
            orderState.NextSequence != message.Sequence)
        {
            throw new InvalidOperationException(
                $"Inbox message '{message.Id}' sequence {message.Sequence} is not the claimed next sequence {orderState.NextSequence} for key '{message.OrderKey}'.");
        }

        return orderState;
    }

    public async Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken)
    {
        var dbContext = _session;

        await dbContext.Set<NOFInboxMessage>()
            .Where(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.ErrorMessage, error)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);

        await ReleaseOrderStateClaimAsync(dbContext, message, error, blocked: false, cancellationToken);

        _logger.LogWarning(
            "Inbox message {InboxId} scheduled for retry #{RetryCount}. Error: {Error}",
            message.Id,
            message.RetryCount,
            error);
    }

    public async Task MarkFailedAsync(
        NOFInboxMessage message,
        string error,
        CancellationToken cancellationToken,
        Exception? ex = null)
    {
        var dbContext = _session;
        var failedAt = DateTime.UtcNow;

        await dbContext.Set<NOFInboxMessage>()
            .Where(m => m.Id == message.Id && m.Route == message.Route && m.Status == InboxMessageStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, InboxMessageStatus.Failed)
                .SetProperty(m => m.ErrorMessage, error)
                .SetProperty(m => m.FailedAtUtc, failedAt)
                .SetProperty(m => m.ClaimedBy, (string?)null)
                .SetProperty(m => m.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);

        await ReleaseOrderStateClaimAsync(dbContext, message, error, blocked: true, cancellationToken);

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

    public async Task ReleaseUnprocessedOrderedMessagesAsync(
        IEnumerable<NOFInboxMessage> messages,
        CancellationToken cancellationToken)
    {
        foreach (var group in messages.GroupBy(static message => (message.Route, message.OrderKey)))
        {
            var claimedBy = group.Select(static message => message.ClaimedBy).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(claimedBy))
            {
                continue;
            }

            var sequences = group
                .Where(static message => message.Sequence.HasValue)
                .Select(static message => message.Sequence!.Value)
                .ToArray();
            if (sequences.Length == 0)
            {
                continue;
            }

            var dbContext = _session;
            await dbContext.Set<NOFInboxMessage>()
                .Where(message => message.Route == group.Key.Route &&
                                  message.OrderKey == group.Key.OrderKey &&
                                  message.Sequence != null &&
                                  sequences.Contains(message.Sequence.Value) &&
                                  message.Status == InboxMessageStatus.Pending &&
                                  message.ClaimedBy == claimedBy)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(message => message.RetryCount, message => message.RetryCount - 1)
                        .SetProperty(message => message.ClaimedBy, (string?)null)
                        .SetProperty(message => message.ClaimExpiresAtUtc, (DateTime?)null),
                    cancellationToken);
        }
    }

    public async Task ReleaseOrderStateClaimAsync(
        string route,
        string orderKey,
        string? claimedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(claimedBy))
        {
            return;
        }

        var dbContext = _session;
        await ReleaseOrderStateClaimAsync(dbContext, route, orderKey, claimedBy, cancellationToken);
    }

    private static async Task ReleaseOrderStateClaimAsync(
        IPersistenceSession dbContext,
        string route,
        string orderKey,
        string claimedBy,
        CancellationToken cancellationToken)
    {
        await dbContext.Set<NOFInboxOrderState>()
            .Where(state => state.Route == route &&
                            state.OrderKey == orderKey &&
                            state.ClaimedBy == claimedBy)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.ClaimedBy, (string?)null)
                    .SetProperty(state => state.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);
    }

    private static async Task ReleaseOrderStateClaimAsync(
        IPersistenceSession dbContext,
        NOFInboxMessage message,
        string error,
        bool blocked,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.OrderKey))
        {
            return;
        }

        var states = dbContext.Set<NOFInboxOrderState>()
            .Where(state => state.Route == message.Route && state.OrderKey == message.OrderKey);
        var updatedAtUtc = DateTime.UtcNow;
        if (blocked)
        {
            await states.ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.ClaimedBy, (string?)null)
                    .SetProperty(state => state.ClaimExpiresAtUtc, (DateTime?)null)
                    .SetProperty(state => state.UpdatedAtUtc, updatedAtUtc)
                    .SetProperty(state => state.ErrorMessage, error)
                    .SetProperty(state => state.BlockedSequence, message.Sequence),
                cancellationToken);
            return;
        }

        await states.ExecuteUpdateAsync(setters => setters
                .SetProperty(state => state.ClaimedBy, (string?)null)
                .SetProperty(state => state.ClaimExpiresAtUtc, (DateTime?)null)
                .SetProperty(state => state.UpdatedAtUtc, updatedAtUtc)
                .SetProperty(state => state.ErrorMessage, error),
            cancellationToken);
    }

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

        await TransactionalMessageRecovery.MarkExpiredExhaustedInboxMessagesAsFailedAsync(
            dbContext,
            _options.MaxRetryCount,
            now,
            cancellationToken);

        await dbContext.Set<NOFInboxOrderState>()
            .Where(state => state.CompletedAtUtc == null &&
                            state.BlockedSequence == null &&
                            dbContext.Set<NOFInboxMessage>().Any(message =>
                                message.Route == state.Route &&
                                message.OrderKey == state.OrderKey &&
                                message.Sequence == state.NextSequence &&
                                message.Status == InboxMessageStatus.Failed))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.BlockedSequence, state => state.NextSequence)
                    .SetProperty(state => state.ErrorMessage, "The next ordered inbox message failed permanently.")
                    .SetProperty(state => state.UpdatedAtUtc, now)
                    .SetProperty(state => state.ClaimedBy, (string?)null)
                    .SetProperty(state => state.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);

        var claimedCount = 0;

        var candidateStates = await dbContext.Set<NOFInboxOrderState>()
            .AsNoTracking()
            .Where(state => state.CompletedAtUtc == null &&
                            state.BlockedSequence == null &&
                            (state.ClaimedBy == null || state.ClaimExpiresAtUtc == null || state.ClaimExpiresAtUtc <= now) &&
                            dbContext.Set<NOFInboxMessage>().Any(message =>
                                message.Route == state.Route &&
                                message.OrderKey == state.OrderKey &&
                                message.Sequence == state.NextSequence &&
                                message.Status == InboxMessageStatus.Pending &&
                                message.RetryCount < _options.MaxRetryCount &&
                                (message.ClaimedBy == null ||
                                 message.ClaimExpiresAtUtc == null ||
                                 message.ClaimExpiresAtUtc <= now)))
            .OrderBy(state => state.UpdatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        var claimedStates = new List<NOFInboxOrderState>(candidateStates.Count);
        foreach (var state in candidateStates)
        {
            var nextMessageExists = await dbContext.Set<NOFInboxMessage>()
                .AsNoTracking()
                .Where(message => message.Route == state.Route &&
                                  message.OrderKey == state.OrderKey &&
                                  message.Sequence == state.NextSequence &&
                                  message.Status == InboxMessageStatus.Pending &&
                                  message.RetryCount < _options.MaxRetryCount &&
                                  (message.ClaimedBy == null || message.ClaimExpiresAtUtc == null || message.ClaimExpiresAtUtc <= now))
                .AnyAsync(cancellationToken);
            if (!nextMessageExists)
            {
                continue;
            }

            var stateClaimed = await dbContext.Set<NOFInboxOrderState>()
                .Where(candidate => candidate.Route == state.Route &&
                                    candidate.OrderKey == state.OrderKey &&
                                    candidate.NextSequence == state.NextSequence &&
                                    candidate.CompletedAtUtc == null &&
                                    candidate.BlockedSequence == null &&
                                    (candidate.ClaimedBy == null ||
                                     candidate.ClaimExpiresAtUtc == null ||
                                     candidate.ClaimExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.ClaimedBy, lockId)
                        .SetProperty(candidate => candidate.ClaimExpiresAtUtc, expiresAt),
                    cancellationToken);
            if (stateClaimed > 0)
            {
                claimedStates.Add(state);
            }
        }

        var claimedStateKeysWithMessages = new HashSet<(string Route, string OrderKey)>();

        foreach (var state in claimedStates)
        {
            if (claimedCount >= batchSize)
            {
                break;
            }

            var candidates = await dbContext.Set<NOFInboxMessage>()
                .AsNoTracking()
                .Where(message => message.Route == state.Route &&
                                  message.OrderKey == state.OrderKey &&
                                  message.Sequence >= state.NextSequence &&
                                  message.Status == InboxMessageStatus.Pending &&
                                  message.RetryCount < _options.MaxRetryCount &&
                                  (message.ClaimedBy == null || message.ClaimExpiresAtUtc == null || message.ClaimExpiresAtUtc <= now))
                .OrderBy(message => message.Sequence)
                .Take(batchSize - claimedCount)
                .ToListAsync(cancellationToken);

            var contiguousSequences = new List<long>(candidates.Count);
            var expectedSequence = state.NextSequence;
            foreach (var candidate in candidates)
            {
                if (candidate.Sequence != expectedSequence)
                {
                    break;
                }

                contiguousSequences.Add(expectedSequence);
                expectedSequence++;
            }

            if (contiguousSequences.Count == 0)
            {
                await ReleaseOrderStateClaimAsync(
                    dbContext,
                    state.Route,
                    state.OrderKey,
                    lockId,
                    cancellationToken);
                continue;
            }

            var claimedForState = await dbContext.Set<NOFInboxMessage>()
                .Where(message => message.Route == state.Route &&
                                  message.OrderKey == state.OrderKey &&
                                  message.Sequence != null &&
                                  contiguousSequences.Contains(message.Sequence.Value) &&
                                  message.Status == InboxMessageStatus.Pending &&
                                  (message.ClaimedBy == null || message.ClaimExpiresAtUtc == null || message.ClaimExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(message => message.RetryCount, message => message.RetryCount + 1)
                        .SetProperty(message => message.ClaimedBy, lockId)
                        .SetProperty(message => message.ClaimExpiresAtUtc, expiresAt),
                    cancellationToken);
            if (claimedForState > 0)
            {
                claimedCount += claimedForState;
                claimedStateKeysWithMessages.Add((state.Route, state.OrderKey));
            }
        }

        foreach (var state in claimedStates.Where(state =>
                     !claimedStateKeysWithMessages.Contains((state.Route, state.OrderKey))))
        {
            await ReleaseOrderStateClaimAsync(
                dbContext,
                state.Route,
                state.OrderKey,
                lockId,
                cancellationToken);
        }

        if (claimedCount < batchSize)
        {
            claimedCount += await dbContext.Set<NOFInboxMessage>()
                .Where(m => m.Status == InboxMessageStatus.Pending &&
                            m.OrderKey == null &&
                            m.Sequence == null &&
                            m.RetryCount < _options.MaxRetryCount &&
                            (m.ClaimedBy == null || m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Id)
                .Take(batchSize - claimedCount)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                        .SetProperty(m => m.ClaimedBy, lockId)
                        .SetProperty(m => m.ClaimExpiresAtUtc, expiresAt),
                    cancellationToken);
        }

        if (claimedCount == 0)
        {
            foreach (var state in claimedStates)
            {
                await ReleaseOrderStateClaimAsync(
                    dbContext,
                    state.Route,
                    state.OrderKey,
                    lockId,
                    cancellationToken);
            }

            yield break;
        }

        var claimed = await dbContext.Set<NOFInboxMessage>()
            .AsNoTracking()
            .Where(m => m.ClaimedBy == lockId)
            .OrderBy(m => m.OrderKey == null ? 1 : 0)
            .ThenBy(m => m.Route)
            .ThenBy(m => m.OrderKey)
            .ThenBy(m => m.Sequence)
            .ThenBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var msgFromDb in claimed)
        {
            yield return msgFromDb;
        }
    }

}
