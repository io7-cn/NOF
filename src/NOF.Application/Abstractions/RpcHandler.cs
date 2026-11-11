using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NOF.Application;

/// <summary>
/// Untyped base class for one split RPC handler.
/// </summary>
public abstract class RpcHandler
{
    /// <summary>
    /// Gets the request type handled by this RPC operation.
    /// </summary>
    public abstract Type RequestType { get; }

    /// <summary>
    /// Gets the response type produced by this RPC operation.
    /// </summary>
    public abstract Type ResponseType { get; }

    /// <summary>
    /// Executes the handler using an untyped request instance.
    /// </summary>
    public abstract Task<IUnion> HandleAsync(object request, Context context, CancellationToken cancellationToken);
}

/// <summary>
/// Typed base class for one split RPC handler.
/// </summary>
public abstract class RpcHandler<TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse> : RpcHandler
    where TResponse : IRpcResponse<TResponse>
{
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);

    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);

    /// <inheritdoc />
    public sealed override async Task<IUnion> HandleAsync(object request, Context context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await HandleAsync((TRequest)request, context, cancellationToken).ConfigureAwait(false);
    }

    protected TResponse Success(TResponse value)
        => value;

    protected TResponse Fail(
        string errorCode,
        string message)
        => TResponse.CreateFailure(Result.Fail(errorCode, message));

    /// <summary>Adapts a transport-independent use case into this RPC operation's response contract.</summary>
    /// <remarks>Existing RPC transport authentication and tenant policies still execute before this bridge.</remarks>
    protected static async Task<TResponse> ExecuteUseCaseAsync<TUseCase>(
        IRequestExecutor executor, TRequest request, Context context, CancellationToken cancellationToken)
        where TUseCase : class, IRequestHandler<TRequest, TResponse>
        => await ExecuteUseCaseAsync<TUseCase, TResponse>(executor, request, context, static response => response, cancellationToken).ConfigureAwait(false);

    /// <summary>Projects a use case's business value into the RPC contract without coupling the use case to RPC.</summary>
    protected static async Task<TResponse> ExecuteUseCaseAsync<TUseCase, TBusinessResponse>(
        IRequestExecutor executor, TRequest request, Context context,
        Func<TBusinessResponse, TResponse> projectResponse, CancellationToken cancellationToken)
        where TUseCase : class, IRequestHandler<TRequest, TBusinessResponse>
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(projectResponse);
        var execution = await executor.ExecuteAsync<TUseCase, TRequest, TBusinessResponse>(request, context, cancellationToken).ConfigureAwait(false);
        return execution switch
        {
            RequestCompleted<TBusinessResponse> completed => projectResponse(completed.Response),
            RequestRejected rejected => TResponse.CreateFailure(rejected.Failure),
            null => throw new InvalidOperationException("The application executor returned no outcome.")
        };
    }

    /// <summary>
    /// Executes the handler using the strongly typed request.
    /// </summary>
    public abstract Task<TResponse> HandleAsync(TRequest request, Context context, CancellationToken cancellationToken);
}
