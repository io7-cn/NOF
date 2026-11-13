using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Optional default engine; execution and descriptions share exactly one effective plan.</summary>
public sealed class InMemoryInvocationPipeline(IServiceProvider services, InvocationPlanFactory plans) : IInvocationPipeline
{
    /// <inheritdoc />
    public InvocationPlan Describe(ServiceOperationDescriptor operation, InvocationOrigin origin) => plans.Describe(operation, origin);
    /// <inheritdoc />
    public InvocationPlan Describe(InvocationContext context) => plans.Describe(context);
    /// <inheritdoc />
    public async ValueTask<object?> ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Describe(context);
        InvocationDelegate pipeline = async (current, value, token) =>
        {
            ValidateIdentity(context, current);
            token.ThrowIfCancellationRequested();
            using var ambient = Context.PushCurrent(current.Context);
            return await terminal(current, value, token).ConfigureAwait(false);
        };
        for (var index = plan.Filters.Count - 1; index >= 0; index--)
        {
            var filter = plan.Filters[index];
            var next = pipeline;
            pipeline = async (current, value, token) =>
            {
                ValidateIdentity(context, current);
                token.ThrowIfCancellationRequested();
                using var ambient = Context.PushCurrent(current.Context);
                return plan.Validate(await filter.Resolve(services).InvokeAsync(current, value, next, token).ConfigureAwait(false));
            };
        }
        return plan.Validate(await pipeline(context, input, cancellationToken).ConfigureAwait(false));
    }
    private static void ValidateIdentity(InvocationContext original, InvocationContext current)
    {
        if (original.Operation != current.Operation || original.HandlerType != current.HandlerType
            || original.HandlerMethodInfo != current.HandlerMethodInfo || original.Origin != current.Origin
            || original.InvocationId != current.InvocationId)
        {
            throw new InvalidOperationException("A filter may normalize semantic context, not replace invocation identity or operation metadata.");
        }
    }
}

/// <summary>Validates the selected default engine before any hosted service starts.</summary>
public sealed class InvocationPlanValidationService(InvocationPlanFactory plans) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) { plans.ValidateConfiguration(); return Task.CompletedTask; }
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Selects optional in-memory execution without overriding a custom host engine.</summary>
public static class InMemoryInvocationPipelineExtensions
{
    /// <summary>Registers the default engine and construction-free plan validation.</summary>
    public static IServiceCollection UseInMemoryInvocationPipeline(this IServiceCollection services)
    {
        services.AddOptions<TenantAuthorizationOptions>();
        services.TryAddSingleton<ApplicationServiceRegistry>();
        services.GetOrAddSingleton<InvocationFilterRegistry>();
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TenantAuthorizationOptions>>().Value;
            var authorization = services.LastOrDefault(item => !item.IsKeyedService && item.ServiceType == typeof(IInboundAuthorizationHandler));
            var engine = services.LastOrDefault(item => !item.IsKeyedService && item.ServiceType == typeof(IInvocationPipeline));
            return new InvocationPlanHostConfiguration(authorization is not null && authorization.ImplementationType != typeof(DefaultInboundAuthorizationHandler),
                options.RequireAuthenticatedTenant, options.RejectMismatchedTenant, engine?.ImplementationType == typeof(InMemoryInvocationPipeline));
        });
        services.TryAddSingleton<InvocationPlanFactory>();
        services.TryAddScoped<IInvocationPipeline, InMemoryInvocationPipeline>();
        services.TryAddScoped<IInvocationPlanProvider>(provider => provider.GetRequiredService<IInvocationPipeline>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InvocationPlanValidationService>());
        return services;
    }
}
