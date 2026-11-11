using NOF.Contract;

namespace NOF.Application;

/// <summary>Provides an optional base class for a transport-independent application use case.</summary>
/// <typeparam name="TRequest">The application input.</typeparam>
/// <typeparam name="TResponse">The business response.</typeparam>
public abstract class RequestHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
{
    /// <inheritdoc />
    public abstract Task<TResponse> HandleAsync(TRequest request, Context context, CancellationToken cancellationToken);
}
