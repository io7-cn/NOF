using NOF.Contract;

namespace NOF.Application;

/// <summary>Continues application execution, optionally with normalized request/context values.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The business response type.</typeparam>
public delegate Task<RequestExecution<TResponse>> RequestExecutionDelegate<TRequest, TResponse>(
    TRequest request, Context context, CancellationToken cancellationToken);
