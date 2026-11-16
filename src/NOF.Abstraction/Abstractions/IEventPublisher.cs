using System.Diagnostics.CodeAnalysis;

namespace NOF.Abstraction;

/// <summary>
/// Publishes in-memory events to handlers resolved from the current scope.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(object payload, Type[] eventTypes, Context context, CancellationToken cancellationToken);
}
