namespace NOF.Infrastructure;

/// <summary>Stages and transitions reliable outbound messages without exposing provider queries.</summary>
public interface IOutboxStore
{
    /// <summary>Stages an outbound message without saving or dispatching it.</summary>
    void Stage(NOFOutboxMessage message);
    /// <summary>Claims an eligible bounded batch while preserving lease ownership.</summary>
    IAsyncEnumerable<NOFOutboxMessage> AtomicClaimPendingMessagesAsync(int batchSize = 100, TimeSpan? claimTimeout = null, CancellationToken cancellationToken = default);
    /// <summary>Releases unprocessed claims and restores their attempt count.</summary>
    ValueTask AtomicReleaseClaimsAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default);
    /// <summary>Marks successfully delivered pending messages as sent.</summary>
    ValueTask<int> AtomicMarkAsSentAsync(IEnumerable<NOFOutboxMessage> messages, CancellationToken cancellationToken = default);
    /// <summary>Records failure and releases the claim or permanently fails an exhausted message.</summary>
    ValueTask AtomicRecordDeliveryFailureAsync(NOFOutboxMessage message, string errorMessage, bool permanent = false, CancellationToken cancellationToken = default);
    /// <summary>Deletes expired completed records while preserving pending and failed records.</summary>
    Task<int> CleanupAsync(DateTime olderThan, CancellationToken cancellationToken);
}
