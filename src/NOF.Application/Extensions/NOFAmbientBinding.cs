using NOF.Abstraction;
using NOF.Application;
using NOF.Domain;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Restores all prior ambient values when a synchronous or asynchronous flow leaves a NOF boundary.</summary>
public sealed class NOFAmbientBinding : IDisposable
{
    private readonly IDisposable _context;
    private readonly IDisposable _mapper;
    private readonly IDisposable _generator;
    private readonly IDisposable _publisher;
    private bool _disposed;
    /// <summary>Binds services lazily so unused conveniences cannot construct dependencies before invocation policies.</summary>
    public NOFAmbientBinding(IServiceProvider services, Context context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        _context = Context.PushCurrent(context);
        _mapper = Mapper.PushCurrent(services);
        _generator = IdGenerator.PushCurrent(services);
        _publisher = EventPublisher.PushCurrent(services);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _publisher.Dispose();
        _generator.Dispose();
        _mapper.Dispose();
        _context.Dispose();
    }
}
