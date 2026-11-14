using NOF.Abstraction;

namespace NOF.Application;

/// <summary>Stages an execution intent in the same unit of work as the caller's business changes.</summary>
public interface IOutbox
{
    /// <summary>Snapshots a message and returns its stable ID without saving or invoking external systems.</summary>
    /// <param name="message">The intent to snapshot.</param>
    /// <param name="context">The caller's semantic context; arbitrary items and identity are not persisted.</param>
    /// <param name="headers">Explicit durable metadata, copied at enqueue; keys are case-insensitive. NOF-managed message ID, tenant and trace keys are reserved.</param>
    /// <param name="cancellationToken">Cancels staging before any record is added.</param>
    /// <remarks>Delivery is at least once with no ordering guarantee. Infrastructure owns any ordering requirements.</remarks>
    ValueTask<Guid> EnqueueAsync<TMessage>(TMessage message, Context context,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default);
}
