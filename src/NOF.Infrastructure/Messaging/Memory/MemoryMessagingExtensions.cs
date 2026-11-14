using Microsoft.Extensions.DependencyInjection;

namespace NOF.Infrastructure;

/// <summary>Registers endpoints owned by the in-process memory transport.</summary>
public static class MemoryMessagingExtensions
{
    /// <summary>Enables durable memory-message acceptance and maps its local endpoints.</summary>
    public static IServiceCollection AddMemoryMessaging(this IServiceCollection services, Action<MemoryEndpointsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddInbox();
        configure(new(services, services.GetOrAddSingleton<MemoryEndpointRegistry>()));
        return services;
    }
}
