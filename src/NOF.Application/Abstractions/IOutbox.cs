using NOF.Abstraction;

namespace NOF.Application;

/// <summary>Stages an execution intent in the same unit of work as the caller's business changes.</summary>
public interface IOutbox
{
    /// <summary>Snapshots a message and returns its stable ID without saving or invoking external systems.</summary>
    ValueTask<Guid> EnqueueAsync<TMessage>(TMessage message, Context context,
        OutboxEnqueueOptions? options = null, CancellationToken cancellationToken = default);
}
