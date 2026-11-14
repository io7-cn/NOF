using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace NOF.Sample.Identity;

/// <summary>Seeds demo clients after the existing database migration lifecycle completes.</summary>
public sealed class SampleIdentityBootstrap(IServiceScopeFactory scopes, IOptions<OpenIddictServerOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await SeedClientsAsync(scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(), options.Value.Issuer!, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedClientsAsync(IOpenIddictApplicationManager manager, Uri issuer, CancellationToken ct = default)
    {
        if (await manager.FindByClientIdAsync("nof-sample-ui", ct) is not null)
        {
            return;
        }
        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "nof-sample-ui",
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "NOF Sample UI",
            RedirectUris = { new Uri(issuer, "/oauth/callback"), new Uri("https://client.local/oauth/callback") },
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.DeviceAuthorization,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.GrantTypes.DeviceCode,
                Permissions.ResponseTypes.Code, Permissions.Scopes.Profile, Permissions.Scopes.Email,
                Permissions.Prefixes.Scope + "sample.read", Permissions.Prefixes.Scope + "sample.write"
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        }, ct);
    }
}
