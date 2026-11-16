using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Selects optional in-memory execution without overriding a custom host engine.</summary>
public static class InMemoryInvocationPipelineExtensions
{
    /// <summary>Registers the default engine and construction-free plan validation.</summary>
    public static IServiceCollection UseInMemoryInvocationPipeline(this IServiceCollection services)
    {
        services.AddNOFAbstraction();
        services.AddOptions<TenantAuthorizationOptions>();
        services.GetOrAddSingleton<ApplicationServiceRegistry>();
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
