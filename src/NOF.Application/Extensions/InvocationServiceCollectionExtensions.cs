using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers invocation extension definitions without selecting a host execution engine.</summary>
public static class InvocationServiceCollectionExtensions
{
    /// <summary>Registers one scoped filter definition. The host-selected pipeline controls execution.</summary>
    public static IServiceCollection AddInvocationFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter>(
        this IServiceCollection services, int order = 0) where TFilter : class, IInvocationFilter
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<TFilter>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(InvocationFilterRegistration)
            && descriptor.ImplementationInstance is InvocationFilterRegistration registration && registration.FilterType == typeof(TFilter)))
        {
            services.AddSingleton(new InvocationFilterRegistration(typeof(TFilter),
                static provider => provider.GetRequiredService<TFilter>(), order));
        }
        return services;
    }
}
