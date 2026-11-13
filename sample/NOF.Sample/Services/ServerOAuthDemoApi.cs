using NOF.Abstraction;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the OAuth UI port to validated server operations.</summary>
public sealed class ServerOAuthDemoApi(IServiceProvider services) : IOAuthDemoApi
{
    public Task<UiResponse<CreateDemoOAuthClientResponse>> CreateClientAsync(CreateDemoOAuthClientRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<CreateDemoOAuthClientResponse>(OAuthChainDemoService.CreateClient.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<DemoTokenResponse>> GetClientTokenAsync(GetDemoClientTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<DemoTokenResponse>(OAuthChainDemoService.GetClientToken.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<DemoTokenResponse>> GetUserTokenAsync(GetDemoUserTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<DemoTokenResponse>(OAuthChainDemoService.GetUserToken.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<DemoTokenResponse>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<DemoTokenResponse>(OAuthChainDemoService.ExchangeToken.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<ConsumeDemoAccessTokenResponse>> CallDownstreamAsync(CallDemoDownstreamRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<ConsumeDemoAccessTokenResponse>(OAuthChainDemoService.CallDownstream.InvokeAsync(services, request, context, cancellationToken));
}
