using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Test;

public sealed class NOFTestHost : IAsyncDisposable, IDisposable
{
    public NOFTestHost(IHost host)
    {
        Host = host;
    }

    public IHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    public NOFTestScope CreateScope()
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        return new NOFTestScope(scope);
    }

    public NOFTestScope CreateScope(Action<NOFTestScope> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var scope = CreateScope();
        configure(scope);
        return scope;
    }

    public async Task ExecuteAsync(Func<NOFTestScope, Task> scenario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        await using var scope = CreateScope();
        cancellationToken.ThrowIfCancellationRequested();
        await scenario(scope);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<NOFTestScope, Task<TResult>> scenario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        await using var scope = CreateScope();
        cancellationToken.ThrowIfCancellationRequested();
        return await scenario(scope);
    }

    public async Task<TResult> CallAsync<TResult>(
        Func<IServiceProvider, Context, CancellationToken, Task<TResult>> invocation,
        Action<NOFTestScope>? configure = null,
        Context? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await using var scope = CreateScope();
        configure?.Invoke(scope);
        return await scope.CallAsync(invocation, context, cancellationToken);
    }

    public async Task SendAsync<TCommand>(ServiceOperationReference<TCommand> operation, TCommand command, Context? context = null, CancellationToken cancellationToken = default)
    {
        using var scope = CreateScope();
        await scope.SendAsync(operation, command, context, cancellationToken);
    }

    public async Task PublishAsync<TNotification>(TNotification notification, Context? context = null, CancellationToken cancellationToken = default)
    {
        using var scope = CreateScope();
        await scope.PublishAsync(notification, context, cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            Host.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            Host.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            if (Host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                Host.Dispose();
            }
        }
    }
}
