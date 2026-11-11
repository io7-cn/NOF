using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Runs typed application policies in registration order and resolves handlers at the terminal step.</summary>
public sealed class RequestExecutor(IServiceProvider services) : IRequestExecutor
{
    /// <inheritdoc />
    public async Task<RequestExecution<TResponse>> ExecuteAsync<TUseCase, TRequest, TResponse>(
        TRequest request, Context context, CancellationToken cancellationToken = default)
        where TUseCase : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        RequestExecutionDelegate<TRequest, TResponse> pipeline = async (currentRequest, currentContext, token) =>
        {
            token.ThrowIfCancellationRequested();
            using var ambient = Context.PushCurrent(currentContext);
            var handler = services.GetRequiredService<TUseCase>();
            return new RequestCompleted<TResponse>(
                await handler.HandleAsync(currentRequest, currentContext, token).ConfigureAwait(false));
        };

        var middlewares = services.GetServices<IRequestMiddleware<TUseCase, TRequest, TResponse>>().ToArray();
        for (var index = middlewares.Length - 1; index >= 0; index--)
        {
            var middleware = middlewares[index];
            var next = pipeline;
            pipeline = async (currentRequest, currentContext, token) =>
            {
                token.ThrowIfCancellationRequested();
                using var ambient = Context.PushCurrent(currentContext);
                return await middleware.InvokeAsync(currentRequest, currentContext, next, token).ConfigureAwait(false);
            };
        }

        var execution = await pipeline(request, context, cancellationToken).ConfigureAwait(false);
        if (execution.Value is null)
        {
            throw new InvalidOperationException("An application policy returned an uninitialized execution outcome.");
        }
        return execution;
    }
}
