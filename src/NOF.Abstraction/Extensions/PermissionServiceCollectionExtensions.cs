using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Composes transport-independent permission checks and an explicitly defined permission catalog.</summary>
public static class PermissionServiceCollectionExtensions
{
    /// <summary>Adds definitions and default scoped checks without enabling identity protocols or role/organization storage.</summary>
    public static IServiceCollection AddPermissions(this IServiceCollection services, params PermissionDefinition[] definitions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitions);
        var catalog = services.GetOrAddSingleton<PermissionCatalog>();
        foreach (var definition in definitions)
        {
            catalog.Add(definition);
        }
        services.TryAddScoped<IUserContext, UserContext>();
        services.TryAddScoped<IPermissionGrantProvider, ClaimsPermissionGrantProvider>();
        services.TryAddScoped<IPermissionChecker>(provider => new PermissionChecker(
            provider.GetRequiredService<IUserContext>(), () => provider.GetRequiredService<IPermissionGrantProvider>(),
            provider.GetRequiredService<PermissionCatalog>()));
        return services;
    }
}
