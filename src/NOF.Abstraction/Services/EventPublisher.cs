using System.Diagnostics.CodeAnalysis;

namespace NOF.Abstraction;

/// <summary>
/// Provides convenience access to the ambient <see cref="IEventPublisher"/> for the current async flow.
/// </summary>
/// <remarks>
/// Prefer explicit <see cref="IEventPublisher"/> dependencies in core runtime paths.
/// The ambient publisher exists as a convenience API for in-scope code that wants a lighter call site.
/// </remarks>
public static class EventPublisher
{
    private static readonly AsyncLocal<Func<IEventPublisher?>?> _currentPublisher = new();

    /// <summary>
    /// Pushes an ambient <see cref="IEventPublisher"/> into the current async flow for convenience API usage.
    /// </summary>
    /// <remarks>A null publisher explicitly shadows the previous binding until disposal.</remarks>
    public static IDisposable PushCurrent(IEventPublisher? publisher) => PushFactory(() => publisher);

    private static IDisposable PushFactory(Func<IEventPublisher?> resolve)
    {
        var previous = _currentPublisher.Value;
        _currentPublisher.Value = resolve;
        return new AmbientPublisherScope(previous);
    }

    /// <summary>
    /// Lazily resolves the bound scope's <see cref="IEventPublisher"/> into the ambient async flow.
    /// </summary>
    public static IDisposable PushCurrent(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return PushFactory(() => services.GetService(typeof(IEventPublisher)) as IEventPublisher);
    }

    /// <summary>
    /// Publishes an event through the ambient publisher convenience API.
    /// </summary>
    public static void PublishEvent(object payload, Type[] eventTypes, Context context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(eventTypes);
        ArgumentNullException.ThrowIfNull(context);

        var publisher = GetCurrentPublisher();
        publisher.PublishAsync(payload, eventTypes, context, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Publishes an event through the ambient publisher with its bound context.
    /// </summary>
    public static void PublishEvent(object payload, Type[] eventTypes)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(eventTypes);

        var publisher = GetCurrentPublisher();
        publisher.PublishAsync(payload, eventTypes, Context.Current, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public static void PublishEvent(
        object payload,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type runtimeType,
        Context context)
    {
        ArgumentNullException.ThrowIfNull(runtimeType);
        PublishEvent(payload, runtimeType.GetAllAssignableTypes(), context);
    }

    public static void PublishEvent(
        object payload,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type runtimeType)
    {
        ArgumentNullException.ThrowIfNull(runtimeType);
        PublishEvent(payload, runtimeType.GetAllAssignableTypes());
    }

    public static void PublishEvent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TPayload>(
        TPayload payload,
        Context context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        PublishEvent(payload, typeof(TPayload), context);
    }

    public static void PublishEvent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TPayload>(TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        PublishEvent(payload, typeof(TPayload));
    }

    internal static IDisposable PushPublisherIfNeeded(IEventPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        return PushCurrent(publisher);
    }

    private static IEventPublisher GetCurrentPublisher()
        => _currentPublisher.Value?.Invoke()
            ?? throw new InvalidOperationException("No ambient IEventPublisher is available for the current async flow.");

    private sealed class AmbientPublisherScope : IDisposable
    {
        private readonly Func<IEventPublisher?>? _previous;
        private bool _disposed;

        public AmbientPublisherScope(Func<IEventPublisher?>? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _currentPublisher.Value = _previous;
            _disposed = true;
        }
    }

}
