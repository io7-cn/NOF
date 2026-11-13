namespace NOF.Domain;

/// <summary>
/// Provides convenience access to the ambient <see cref="IIdGenerator"/> for the current async flow.
/// </summary>
/// <remarks>
/// Prefer explicit <see cref="IIdGenerator"/> dependencies in core runtime paths.
/// The ambient generator exists as a convenience API for source-generated <c>New()</c> helpers and similar call sites.
/// </remarks>
public static class IdGenerator
{
    private static readonly AsyncLocal<Func<IIdGenerator?>?> _currentGenerator = new();

    /// <summary>
    /// Gets the ambient <see cref="IIdGenerator"/> instance for the current async flow.
    /// </summary>
    public static IIdGenerator Current
    {
        get => _currentGenerator.Value?.Invoke() ?? throw new InvalidOperationException(
            "No ambient IIdGenerator is available for the current async flow.");
    }

    /// <summary>
    /// Pushes an ambient <see cref="IIdGenerator"/> into the current async flow for convenience API usage.
    /// </summary>
    /// <remarks>A null generator explicitly shadows the previous binding until disposal.</remarks>
    public static IDisposable PushCurrent(IIdGenerator? generator) => PushFactory(() => generator);

    private static IDisposable PushFactory(Func<IIdGenerator?> resolve)
    {
        var previous = _currentGenerator.Value;
        _currentGenerator.Value = resolve;
        return new AmbientIdGeneratorScope(previous);
    }

    /// <summary>
    /// Lazily resolves the bound scope's <see cref="IIdGenerator"/> into the ambient async flow.
    /// </summary>
    public static IDisposable PushCurrent(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return PushFactory(() => services.GetService(typeof(IIdGenerator)) as IIdGenerator);
    }

    private sealed class AmbientIdGeneratorScope : IDisposable
    {
        private readonly Func<IIdGenerator?>? _previousGenerator;
        private bool _disposed;

        public AmbientIdGeneratorScope(Func<IIdGenerator?>? previousGenerator)
        {
            _previousGenerator = previousGenerator;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _currentGenerator.Value = _previousGenerator;
            _disposed = true;
        }
    }
}
