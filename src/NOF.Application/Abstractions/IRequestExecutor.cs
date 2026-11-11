using NOF.Contract;

namespace NOF.Application;

/// <summary>Executes a typed application request through its policies and scoped handler.</summary>
/// <remarks>The concrete use case identifies execution; different use cases can share request and response types.</remarks>
public interface IRequestExecutor
{
    /// <summary>Runs policies before resolving the handler and preserves its declared response shape.</summary>
    /// <typeparam name="TUseCase">The scoped use case implementation to execute.</typeparam>
    /// <typeparam name="TRequest">The application input.</typeparam>
    /// <typeparam name="TResponse">The unchanged business response.</typeparam>
    Task<RequestExecution<TResponse>> ExecuteAsync<TUseCase, TRequest, TResponse>(
        TRequest request, Context context, CancellationToken cancellationToken = default)
        where TUseCase : class, IRequestHandler<TRequest, TResponse>;
}
