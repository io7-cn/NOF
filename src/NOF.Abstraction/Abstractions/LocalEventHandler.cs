using System.ComponentModel;

namespace NOF.Abstraction;

/// <summary>
/// Non-generic base type for in-memory event handlers. Not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class LocalEventHandler
{
    public abstract Task HandleAsync(object @event, Context context, CancellationToken cancellationToken);
}

/// <summary>
/// Handles in-memory events of the specified type within the current scope.
/// </summary>
/// <typeparam name="TEvent">The event payload type.</typeparam>
public abstract class LocalEventHandler<TEvent> : LocalEventHandler
{
    /// <inheritdoc />
    public sealed override Task HandleAsync(object @event, Context context, CancellationToken cancellationToken)
        => HandleAsync((TEvent)@event, context, cancellationToken);

    /// <summary>Handles the event.</summary>
    /// <param name="event">The event payload.</param>
    /// <param name="context">The context supplied by the event publisher.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public abstract Task HandleAsync(TEvent @event, Context context, CancellationToken cancellationToken);
}
