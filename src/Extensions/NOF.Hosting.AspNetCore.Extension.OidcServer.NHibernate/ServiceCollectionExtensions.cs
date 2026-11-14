using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Hosting.AspNetCore.Extension.OidcServer.NHibernate;
using NOF.Infrastructure.NHibernate;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers native OIDC state storage in the selected provider's existing scope.</summary>
public static class OidcNHibernateExtensions
{
    /// <summary>Shares the selected provider's native session and unit of work; configure it before building the host.</summary>
    public static IServiceCollection AddOidcNHibernate(this IServiceCollection services)
    {
        services.AddScoped<IOAuthClientStore, NHibernateOAuthClientStore>();
        services.AddScoped<ISigningKeyStore, NHibernateSigningKeyStore>();
        services.AddScoped<IRevokedRefreshTokenStore, NHibernateRevokedRefreshTokenStore>();
        services.AddSingleton(OidcModelConfigurations.Configure);
        return services;
    }
}
