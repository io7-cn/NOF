using NOF.Contract;

namespace NOF.Application;

/// <summary>Applies an application policy independently of HTTP, RPC or message-delivery mechanics.</summary>
/// <typeparam name="TUseCase">The use case identity. Shared input/output types do not share policies implicitly.</typeparam>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The business response type.</typeparam>
public interface IRequestMiddleware<TUseCase, TRequest, TResponse>
    where TUseCase : class, IRequestHandler<TRequest, TResponse>
{
    /// <summary>Runs a policy, continues execution, or returns an explicit rejection.</summary>
    Task<RequestExecution<TResponse>> InvokeAsync(
        TRequest request, Context context, RequestExecutionDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken);
}
