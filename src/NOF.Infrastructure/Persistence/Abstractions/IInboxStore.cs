namespace NOF.Infrastructure;

/// <summary>Owns received-message claims, completion and recovery without exposing ORM query state.</summary>
public interface IInboxStore
{
    /// <summary>Persists a received message; callers reconcile duplicate failures in a fresh scope.</summary>
    Task ReceiveAsync(NOFInboxMessage message, CancellationToken cancellationToken);
    /// <summary>Checks durable receipt identity for duplicate reconciliation.</summary>
    Task<bool> ExistsAsync(Guid messageId, string route, CancellationToken cancellationToken);
    /// <summary>Claims an eligible bounded batch while preserving lease ownership.</summary>
    IAsyncEnumerable<NOFInboxMessage> AtomicClaimPendingMessagesAsync(int batchSize, TimeSpan claimTimeout, CancellationToken cancellationToken = default);
    /// <summary>Loads the pending receipt tracked by the current invocation transaction.</summary>
    Task<NOFInboxMessage?> FindPendingAsync(NOFInboxMessage message, CancellationToken cancellationToken);
    /// <summary>Records a retry after the failed invocation scope has been discarded.</summary>
    Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken);
    /// <summary>Records an exhausted failure.</summary>
    Task MarkFailedAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken, Exception? ex = null);
    /// <summary>Deletes expired completed records while preserving pending and failed records.</summary>
    Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken);
}
