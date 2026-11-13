using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Application;

/// <summary>Submits work or publishes facts; completion means acceptance, never operation completion.</summary>
public interface IMessageDispatcher
{
    /// <summary>Submits work to a configured target queue.</summary>
    Task SendAsync<T>(ServiceOperationReference<T> operation, T message, Context context, CancellationToken cancellationToken = default);
    /// <summary>Stages work in the scoped outbox; save the same unit of work to persist it.</summary>
    Task DeferSendAsync<T>(ServiceOperationReference<T> operation, T message, Context context, CancellationToken cancellationToken = default);
    /// <summary>Stages work with an ordered stream shared by all participating consumers.</summary>
    Task DeferSendOrderedAsync<T>(ServiceOperationReference<T> operation, T message, string orderKey, Context context,
        bool completesOrderKey = false, CancellationToken cancellationToken = default);
    /// <summary>Publishes a fact using its configured stable topic.</summary>
    Task PublishAsync<T>(T message, Context context, CancellationToken cancellationToken = default);
    /// <summary>Stages a fact in the scoped outbox.</summary>
    Task DeferPublishAsync<T>(T message, Context context, CancellationToken cancellationToken = default);
    /// <summary>Stages an ordered fact; sequence and outbox intent commit with business changes.</summary>
    Task DeferPublishOrderedAsync<T>(T message, string orderKey, Context context,
        bool completesOrderKey = false, CancellationToken cancellationToken = default);
}
