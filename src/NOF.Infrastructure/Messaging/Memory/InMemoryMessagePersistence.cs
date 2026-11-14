using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Stages and processes memory messages through one scoped unit of work, without an ORM facade.</summary>
public sealed class InMemoryMessagePersistence(InMemoryMessageDatabase database, IOptions<TransactionalMessageOptions> options)
    : IOutboxStore, IInboxStore, IUnitOfWork, ITransactionManager
{
    private readonly InMemoryMessageDatabase.TenantMessages _state = database.ForTenant(TenantId.Normalize(Context.Current.TenantId));
    private readonly List<NOFOutboxMessage> _staged = [];
    private readonly Dictionary<(Guid, string), (NOFInboxMessage Row, string? Owner)> _tracked = [];
    private NOFOutboxMessage[] _flushed = [];
    private (NOFInboxMessage Row, string? Owner)[] _flushedReceipts = [];
    private bool _transaction;

    /// <inheritdoc />
    public void Stage(NOFOutboxMessage message) => _staged.Add(Copy(message));
    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var staged = _staged.Select(Copy).ToArray();
        var receipts = _tracked.Values.Select(item => (Copy(item.Row), item.Owner)).ToArray();
        if (_transaction)
        {
            _flushed = [.. _flushed, .. staged];
            _flushedReceipts = receipts;
        }
        else
        {
            Apply(staged, receipts);
            _tracked.Clear();
        }
        _staged.Clear();
        return Task.CompletedTask;
    }
    private void Apply(NOFOutboxMessage[] staged, (NOFInboxMessage Row, string? Owner)[] receipts)
    {
        lock (_state.Gate)
        {
            if (staged.Select(row => row.Id).Distinct().Count() != staged.Length || staged.Any(row => _state.Outbox.ContainsKey(row.Id)))
            {
                throw new DbUpdateException("A message with the same identity already exists.");
            }
            foreach (var (row, owner) in receipts)
            {
                if (!_state.Inbox.TryGetValue((row.Id, row.Route), out var current) || current.Status != InboxMessageStatus.Pending || owner is null || current.ClaimedBy != owner)
                {
                    throw new DbUpdateConcurrencyException("The receipt claim is no longer owned by this scope.");
                }
            }
            foreach (var row in staged)
            {
                _state.Outbox.Add(row.Id, Copy(row));
            }
            foreach (var (row, _) in receipts)
            {
                _state.Inbox[(row.Id, row.Route)] = Copy(row);
            }
        }
    }
    /// <inheritdoc />
    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_transaction)
        {
            throw new InvalidOperationException("A memory message transaction is already active.");
        }
        _transaction = true;
        return Task.FromResult<ITransaction>(new Transaction(this));
    }
    /// <summary>Owns one scoped memory-message transaction and releases it idempotently.</summary>
    public sealed class Transaction(InMemoryMessagePersistence owner) : ITransaction
    {
        private bool _completed;
        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completed)
            {
                throw new InvalidOperationException("The transaction has completed.");
            }
            owner.Apply(owner._flushed, owner._flushedReceipts);
            return DisposeAsync().AsTask();
        }
        /// <inheritdoc />
        public Task RollbackAsync(CancellationToken cancellationToken = default) => DisposeAsync().AsTask();
        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (_completed)
            {
                return ValueTask.CompletedTask;
            }
            owner._flushed = [];
            owner._flushedReceipts = [];
            owner._staged.Clear();
            owner._tracked.Clear();
            owner._transaction = false;
            _completed = true;
            return ValueTask.CompletedTask;
        }
    }
    /// <inheritdoc />
    public Task ReceiveAsync(NOFInboxMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            if (!_state.Inbox.TryAdd((message.Id, message.Route), Copy(message)))
            {
                throw new DbUpdateException("Duplicate receipt.");
            }
        }
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid messageId, string route, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            return Task.FromResult(_state.Inbox.ContainsKey((messageId, route)));
        }
    }
    /// <inheritdoc />
    public Task<NOFInboxMessage?> FindPendingAsync(NOFInboxMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            if (!_state.Inbox.TryGetValue((message.Id, message.Route), out var row) || row.Status != InboxMessageStatus.Pending || row.ClaimedBy is null || row.ClaimedBy != message.ClaimedBy)
            {
                return Task.FromResult<NOFInboxMessage?>(null);
            }
            var copy = Copy(row);
            _tracked[(row.Id, row.Route)] = (copy, row.ClaimedBy);
            return Task.FromResult<NOFInboxMessage?>(copy);
        }
    }
    /// <inheritdoc />
    public async IAsyncEnumerable<NOFOutboxMessage> AtomicClaimPendingMessagesAsync(int batchSize = 100, TimeSpan? claimTimeout = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NOFOutboxMessage[] claimed;
        lock (_state.Gate)
        {
            var now = DateTime.UtcNow;
            var owner = Guid.NewGuid().ToString();
            var expires = now.Add(claimTimeout ?? options.Value.Outbox.ClaimTimeout);
            foreach (var row in _state.Outbox.Values.Where(row => row.Status == OutboxMessageStatus.Pending && row.RetryCount >= options.Value.Outbox.MaxRetryCount && (row.ClaimedBy is null || row.ClaimExpiresAtUtc is null || row.ClaimExpiresAtUtc <= now)))
            {
                row.Status = OutboxMessageStatus.Failed;
                row.ErrorMessage = "Exceeded max retry count";
                row.FailedAtUtc = now;
                row.ClaimedBy = null;
                row.ClaimExpiresAtUtc = null;
            }
            claimed = _state.Outbox.Values.Where(row => row.Status == OutboxMessageStatus.Pending && row.RetryCount < options.Value.Outbox.MaxRetryCount && (row.NextAttemptAtUtc is null || row.NextAttemptAtUtc <= now) && (row.ClaimedBy is null || row.ClaimExpiresAtUtc is null || row.ClaimExpiresAtUtc <= now))
                .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).Take(batchSize > 0 ? batchSize : options.Value.Outbox.BatchSize).ToArray();
            foreach (var row in claimed)
            {
                row.RetryCount++;
                row.ClaimedBy = owner;
                row.ClaimExpiresAtUtc = expires;
            }
            claimed = claimed.Select(Copy).ToArray();
        }
        await Task.CompletedTask;
        foreach (var row in claimed)
        {
            yield return row;
        }
    }
    /// <inheritdoc />
    public async IAsyncEnumerable<NOFInboxMessage> AtomicClaimPendingMessagesAsync(int batchSize, TimeSpan claimTimeout,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NOFInboxMessage[] claimed;
        lock (_state.Gate)
        {
            var now = DateTime.UtcNow;
            var owner = Guid.NewGuid().ToString();
            var expires = now.Add(claimTimeout);
            foreach (var row in _state.Inbox.Values.Where(row => row.Status == InboxMessageStatus.Pending && row.RetryCount >= options.Value.Inbox.MaxRetryCount && (row.ClaimedBy is null || row.ClaimExpiresAtUtc is null || row.ClaimExpiresAtUtc <= now)))
            {
                row.Status = InboxMessageStatus.Failed;
                row.ErrorMessage = "Exceeded max retry count";
                row.FailedAtUtc = now;
                row.ClaimedBy = null;
                row.ClaimExpiresAtUtc = null;
            }
            claimed = _state.Inbox.Values.Where(row => row.Status == InboxMessageStatus.Pending && row.RetryCount < options.Value.Inbox.MaxRetryCount && (row.ClaimedBy is null || row.ClaimExpiresAtUtc is null || row.ClaimExpiresAtUtc <= now))
                .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).Take(batchSize > 0 ? batchSize : options.Value.Inbox.BatchSize).ToArray();
            foreach (var row in claimed)
            {
                row.RetryCount++;
                row.ClaimedBy = owner;
                row.ClaimExpiresAtUtc = expires;
            }
            claimed = claimed.Select(Copy).ToArray();
        }
        await Task.CompletedTask;
        foreach (var row in claimed)
        {
            yield return row;
        }
    }
    /// <inheritdoc />
    public ValueTask AtomicReleaseClaimsAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            foreach (var message in messages)
            {
                if (Owned(message, out var row))
                {
                    row!.RetryCount--;
                    row.ClaimedBy = null;
                    row.ClaimExpiresAtUtc = null;
                }
            }
        }
        return ValueTask.CompletedTask;
    }
    /// <inheritdoc />
    public ValueTask<int> AtomicMarkAsSentAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = 0;
        lock (_state.Gate)
        {
            foreach (var message in messages)
            {
                if (Owned(message, out var row))
                {
                    row!.Status = OutboxMessageStatus.Sent;
                    row.SentAtUtc = DateTime.UtcNow;
                    row.NextAttemptAtUtc = null;
                    row.ClaimedBy = null;
                    row.ClaimExpiresAtUtc = null;
                    count++;
                }
            }
        }
        return ValueTask.FromResult(count);
    }
    private bool Owned(NOFOutboxMessage message, out NOFOutboxMessage? row)
        => _state.Outbox.TryGetValue(message.Id, out row) && row.Status == OutboxMessageStatus.Pending && message.ClaimedBy is not null && message.ClaimedBy == row.ClaimedBy;
    /// <inheritdoc />
    public ValueTask AtomicRecordDeliveryFailureAsync(NOFOutboxMessage message, string errorMessage, bool permanent = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            if (Owned(message, out var row))
            {
                var now = DateTime.UtcNow;
                var terminal = permanent || message.RetryCount >= options.Value.Outbox.MaxRetryCount;
                row!.Status = terminal ? OutboxMessageStatus.Failed : OutboxMessageStatus.Pending;
                row.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 2048)];
                row.FailedAtUtc = now;
                row.NextAttemptAtUtc = terminal ? null : now.Add(options.Value.Outbox.RetryDelay);
                row.ClaimedBy = null;
                row.ClaimExpiresAtUtc = null;
            }
        }
        return ValueTask.CompletedTask;
    }
    /// <inheritdoc />
    public Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken) => RecordFailure(message, error, false, cancellationToken);
    /// <inheritdoc />
    public Task MarkFailedAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken, Exception? ex = null) => RecordFailure(message, error, true, cancellationToken);
    private Task RecordFailure(NOFInboxMessage message, string error, bool terminal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            if (_state.Inbox.TryGetValue((message.Id, message.Route), out var row) && row.Status == InboxMessageStatus.Pending && message.ClaimedBy is not null && row.ClaimedBy == message.ClaimedBy)
            {
                row.Status = terminal ? InboxMessageStatus.Failed : InboxMessageStatus.Pending;
                row.ErrorMessage = error[..Math.Min(error.Length, 2048)];
                if (terminal)
                {
                    row.FailedAtUtc = DateTime.UtcNow;
                }
                row.ClaimedBy = null;
                row.ClaimExpiresAtUtc = null;
            }
        }
        return Task.CompletedTask;
    }
    Task<int> IInboxStore.CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            var keys = _state.Inbox.Where(pair => pair.Value.Status == InboxMessageStatus.Processed && pair.Value.ProcessedAtUtc < olderThan).Select(pair => pair.Key).ToArray();
            foreach (var key in keys)
            {
                _state.Inbox.Remove(key);
            }
            return Task.FromResult(keys.Length);
        }
    }
    Task<int> IOutboxStore.CleanupAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.Gate)
        {
            var keys = _state.Outbox.Where(pair => pair.Value.Status == OutboxMessageStatus.Sent && pair.Value.SentAtUtc < olderThan).Select(pair => pair.Key).ToArray();
            foreach (var key in keys)
            {
                _state.Outbox.Remove(key);
            }
            return Task.FromResult(keys.Length);
        }
    }
    private static NOFInboxMessage Copy(NOFInboxMessage row) => new()
    {
        Id = row.Id,
        CreatedAtUtc = row.CreatedAtUtc,
        RetryCount = row.RetryCount,
        Route = row.Route,
        Payload = row.Payload.ToArray(),
        Headers = row.Headers,
        ProcessedAtUtc = row.ProcessedAtUtc,
        FailedAtUtc = row.FailedAtUtc,
        ErrorMessage = row.ErrorMessage,
        ClaimedBy = row.ClaimedBy,
        ClaimExpiresAtUtc = row.ClaimExpiresAtUtc,
        Status = row.Status
    };

    private static NOFOutboxMessage Copy(NOFOutboxMessage row) => new()
    {
        Id = row.Id,
        CreatedAtUtc = row.CreatedAtUtc,
        RetryCount = row.RetryCount,
        MessageType = row.MessageType,
        SchemaVersion = row.SchemaVersion,
        NextAttemptAtUtc = row.NextAttemptAtUtc,
        Payload = row.Payload.ToArray(),
        Headers = row.Headers,
        SentAtUtc = row.SentAtUtc,
        FailedAtUtc = row.FailedAtUtc,
        ErrorMessage = row.ErrorMessage,
        ClaimedBy = row.ClaimedBy,
        ClaimExpiresAtUtc = row.ClaimExpiresAtUtc,
        Status = row.Status,
        TraceParent = row.TraceParent
    };
}
