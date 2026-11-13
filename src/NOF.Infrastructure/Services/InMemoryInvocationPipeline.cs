using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Default in-memory execution engine. Other hosts can replace IInvocationPipeline.</summary>
public sealed class InMemoryInvocationPipeline(IServiceProvider services, IEnumerable<InvocationFilterRegistration> registrations, IEnumerable<IInvocationFilter> directFilters) : IInvocationPipeline
{
    private readonly InvocationFilterRegistration[] _filters = registrations.Concat(directFilters.Select(filter => new InvocationFilterRegistration(filter.GetType(), _ => filter, 0)))
        .OrderBy(registration => registration.Order).ToArray();

    /// <inheritdoc />
    public ValueTask ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvocationDelegate pipeline = async (current, value, token) =>
        {
            token.ThrowIfCancellationRequested();
            using var ambient = Context.PushCurrent(current.Context);
            await terminal(current, value, token).ConfigureAwait(false);
        };
        for (var i = _filters.Length - 1; i >= 0; i--)
        {
            var registration = _filters[i];
            var next = pipeline;
            pipeline = async (current, value, token) =>
            {
                token.ThrowIfCancellationRequested();
                using var ambient = Context.PushCurrent(current.Context);
                await registration.Resolve(services).InvokeAsync(current, value, next, token).ConfigureAwait(false);
            };
        }
        return pipeline(context, input, cancellationToken);
    }
}

/// <summary>Selects the optional default invocation engine for a host.</summary>
public static class InMemoryInvocationPipelineExtensions
{
    /// <summary>Registers the in-memory default only when the host has not supplied another engine.</summary>
    public static IServiceCollection UseInMemoryInvocationPipeline(this IServiceCollection services)
    {
        services.TryAddScoped<IInvocationPipeline, InMemoryInvocationPipeline>();
        return services;
    }
}
