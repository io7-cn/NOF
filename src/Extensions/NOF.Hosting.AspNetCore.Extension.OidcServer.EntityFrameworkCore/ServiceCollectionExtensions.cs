using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Hosting.AspNetCore.Extension.OidcServer.EntityFrameworkCore;
using NOF.Infrastructure.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers native OIDC state storage in the selected provider's existing scope.</summary>
public static class OidcEntityFrameworkCoreExtensions
{
    /// <summary>Shares the selected provider's native session and unit of work; configure it before building the host.</summary>
    public static IServiceCollection AddOidcEntityFrameworkCore(this IServiceCollection services)
    {
        services.AddScoped<IOAuthClientStore, EfCoreOAuthClientStore>();
        services.AddScoped<ISigningKeyStore, EfCoreSigningKeyStore>();
        services.AddScoped<IRevokedRefreshTokenStore, EfCoreRevokedRefreshTokenStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEFCoreModelConfiguration, OAuthClientModelCreatingContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEFCoreModelConfiguration, PersistedSigningKeyModelCreatingContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEFCoreModelConfiguration, RevokedRefreshTokenModelCreatingContributor>());
        return services;
    }
}
