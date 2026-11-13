namespace NOF.Infrastructure;

/// <summary>Owns received-message claims, completion ordering and recovery without exposing ORM query state.</summary>
public interface IInboxStore
{
    /// <summary>Persists a received message and its ordering state; callers reconcile duplicate failures in a fresh scope.</summary>
    Task ReceiveAsync(NOFInboxMessage message, InboxMessageOrder? order, CancellationToken cancellationToken);
    /// <summary>Checks durable receipt identity for duplicate reconciliation.</summary>
    Task<bool> ExistsAsync(Guid messageId, string route, CancellationToken cancellationToken);
    /// <summary>Claims an eligible bounded batch while preserving lease and ordering constraints.</summary>
    IAsyncEnumerable<NOFInboxMessage> AtomicClaimPendingMessagesAsync(int batchSize, TimeSpan claimTimeout, CancellationToken cancellationToken = default);
    /// <summary>Loads the pending receipt tracked by the current invocation transaction.</summary>
    Task<NOFInboxMessage?> FindPendingAsync(NOFInboxMessage message, CancellationToken cancellationToken);
    /// <summary>Loads and validates the claimed sequence inside the current invocation transaction.</summary>
    Task<NOFInboxOrderState?> LoadAndValidateOrderStateAsync(NOFInboxMessage message, CancellationToken cancellationToken);
    /// <summary>Records a retry after the failed invocation scope has been discarded.</summary>
    Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken);
    /// <summary>Records an exhausted failure and blocks its ordering key when required.</summary>
    Task MarkFailedAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken, Exception? ex = null);
    /// <summary>Releases later messages without allowing them to overtake a failed predecessor.</summary>
    Task ReleaseUnprocessedOrderedMessagesAsync(IEnumerable<NOFInboxMessage> messages, CancellationToken cancellationToken);
    /// <summary>Releases an ordering-key lease owned by the specified claimant.</summary>
    Task ReleaseOrderStateClaimAsync(string route, string orderKey, string? claimedBy, CancellationToken cancellationToken);
    /// <summary>Deletes expired completed records while preserving live ordering and recovery state.</summary>
    Task<(int Messages, int Orders)> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken);
}
