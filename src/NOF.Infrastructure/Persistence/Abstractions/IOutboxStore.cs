namespace NOF.Infrastructure;

/// <summary>Stages and transitions reliable outbound messages without exposing provider queries.</summary>
public interface IOutboxStore
{
    /// <summary>Stages an outbound message without saving or dispatching it.</summary>
    void Stage(NOFOutboxMessage message);
    /// <summary>Claims an eligible bounded batch while preserving lease and ordering constraints.</summary>
    IAsyncEnumerable<NOFOutboxMessage> AtomicClaimPendingMessagesAsync(int batchSize = 100, TimeSpan? claimTimeout = null, CancellationToken cancellationToken = default);
    /// <summary>Releases unprocessed claims and restores their attempt count.</summary>
    ValueTask AtomicReleaseClaimsAsync(IEnumerable<Guid> messageIds, CancellationToken cancellationToken = default);
    /// <summary>Marks successfully delivered pending messages as sent.</summary>
    ValueTask<int> AtomicMarkAsSentAsync(IEnumerable<Guid> messageIds, CancellationToken cancellationToken = default);
    /// <summary>Records failure and releases the claim or permanently fails an exhausted message.</summary>
    ValueTask AtomicRecordDeliveryFailureAsync(NOFOutboxMessage message, string errorMessage, CancellationToken cancellationToken = default);
    /// <summary>Deletes expired completed records while preserving live ordering and recovery state.</summary>
    Task<(int Messages, int Compacted, int Completed)> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken);
}
