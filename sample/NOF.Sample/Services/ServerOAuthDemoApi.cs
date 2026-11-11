using NOF.Contract;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerOAuthDemoApi(IServiceProvider services) : IOAuthDemoApi
{
    public Task<Result<CreateDemoOAuthClientResponse>> CreateClientAsync(CreateDemoOAuthClientRequest request, Context context, CancellationToken cancellationToken = default)
        => OAuthChainDemoService.CreateClient.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<DemoTokenResponse>> GetClientTokenAsync(GetDemoClientTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => OAuthChainDemoService.GetClientToken.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<DemoTokenResponse>> GetUserTokenAsync(GetDemoUserTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => OAuthChainDemoService.GetUserToken.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<DemoTokenResponse>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => OAuthChainDemoService.ExchangeToken.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<ConsumeDemoAccessTokenResponse>> CallDownstreamAsync(CallDemoDownstreamRequest request, Context context, CancellationToken cancellationToken = default)
        => OAuthChainDemoService.CallDownstream.InvokeAsync(services, request, context, cancellationToken);
}
