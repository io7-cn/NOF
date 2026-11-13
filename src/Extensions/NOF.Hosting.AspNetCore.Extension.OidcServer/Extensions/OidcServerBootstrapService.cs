using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Contract;
using System.Runtime.CompilerServices;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Maps the protocol endpoints during HTTP application composition.</summary>
public sealed class OidcServerEndpointModule : IHttpEndpointModule
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapOidcServer();
}

/// <summary>Seeds configured clients during native host startup, after migration StartingAsync completes.</summary>
public sealed class OidcServerBootstrapService(IServiceProvider services) : IHostedService
{
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId(NOFAbstractionConstants.Tenant.HostId));
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();

        var bootstrapOptions = scope.ServiceProvider.GetRequiredService<IOptions<OidcServerBootstrapOptions>>().Value;
        if (bootstrapOptions.PublicClients.Count == 0 && bootstrapOptions.ConfidentialClients.Count == 0)
        {
            return;
        }

        var clientRepository = scope.ServiceProvider.GetRequiredService<IOAuthClientRepository>();
        foreach (var request in bootstrapOptions.PublicClients
                     .Concat(bootstrapOptions.ConfidentialClients)
                     .GroupBy(static client => client.ClientId, StringComparer.Ordinal)
                     .Select(static group => group.Last()))
        {
            var existing = await clientRepository.GetAsync(request.ClientId, cancellationToken).ConfigureAwait(false);
            if (existing is OAuthClientDescriptor existingSuccess)
            {
                continue;
            }

            if (existing is not Failure { Code: "not_found" })
            {
                EnsureSucceeded(
                    existing,
                    request.ClientId,
                    request.ClientType == OAuthClientType.Public ? "public" : "confidential");
            }

            var createResult = await clientRepository.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            EnsureSucceeded(
                createResult,
                request.ClientId,
                request.ClientType == OAuthClientType.Public ? "public" : "confidential");
        }
    }

    private static void EnsureSucceeded(IUnion result, string clientId, string clientType)
    {
        if (result.Value is not null and not Failure)
        {
            return;
        }

        var failure = result.Value as Failure
            ?? throw new InvalidOperationException("The response contains no outcome case.");
        throw new InvalidOperationException(
            $"Failed to bootstrap OIDC {clientType} client '{clientId}': [{failure.Code}] {failure.Message}");
    }
}
