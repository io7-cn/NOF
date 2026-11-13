namespace NOF.Application;

/// <summary>
/// Provides convenience access to the ambient <see cref="IMapper"/> for the current async flow.
/// </summary>
/// <remarks>
/// The ambient mapper is bound by the current dependency injection scope. Use
/// <see cref="PushCurrent(IMapper)"/> to establish an explicit boundary in standalone code and tests.
/// </remarks>
public static class Mapper
{
    private static readonly AsyncLocal<Func<IMapper?>?> _currentMapper = new();

    /// <summary>
    /// Gets the ambient <see cref="IMapper"/> for the current async flow.
    /// </summary>
    public static IMapper Current
        => _currentMapper.Value?.Invoke() ?? throw new InvalidOperationException(
            "No ambient IMapper is available for the current async flow. " +
            "Enter services.BindNOF(context) or use an explicit IMapper.");

    /// <summary>
    /// Pushes an ambient mapper and restores the previous mapper when the returned scope is disposed.
    /// </summary>
    /// <remarks>A null mapper explicitly shadows the previous binding until disposal.</remarks>
    public static IDisposable PushCurrent(IMapper? mapper) => PushFactory(() => mapper);

    private static IDisposable PushFactory(Func<IMapper?> resolve)
    {
        var previous = _currentMapper.Value;
        _currentMapper.Value = resolve;
        return new AmbientMapperScope(previous);
    }

    /// <summary>
    /// Lazily resolves the bound dependency injection scope's mapper.
    /// </summary>
    public static IDisposable PushCurrent(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return PushFactory(() => services.GetService(typeof(IMapper)) as IMapper);
    }

    private sealed class AmbientMapperScope(Func<IMapper?>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _currentMapper.Value = previous;
            _disposed = true;
        }
    }
}
