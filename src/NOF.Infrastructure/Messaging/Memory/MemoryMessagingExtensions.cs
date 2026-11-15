using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NOF.Infrastructure;

/// <summary>Registers endpoints owned by the in-process memory transport.</summary>
public static class MemoryMessagingExtensions
{
    /// <summary>Maps local operations without durable receipt storage.</summary>
    public static IServiceCollection AddMemoryMessaging(this IServiceCollection services, Action<MemoryEndpointsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddNOFJson();
        services.TryAddScoped<MemoryMessageClient>();
        configure(new(services, services.GetOrAddSingleton<MemoryEndpointRegistry>()));
        return services;
    }
}
