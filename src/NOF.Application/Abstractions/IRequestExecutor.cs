using NOF.Contract;

namespace NOF.Application;

/// <summary>Executes a typed application request through its policies and scoped handler.</summary>
public interface IRequestExecutor
{
    /// <summary>Runs policies before resolving the handler and preserves its declared response shape.</summary>
    Task<RequestExecution<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request, Context context, CancellationToken cancellationToken = default);
}
