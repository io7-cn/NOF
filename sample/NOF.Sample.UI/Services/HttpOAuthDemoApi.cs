using NOF.Contract;

namespace NOF.Sample.UI.Services;

/// <summary>Explicit HTTP integration owned by the sample UI.</summary>
public sealed class HttpOAuthDemoApi(HttpClient httpClient)
    : SampleHttpApi(httpClient), IOAuthDemoApi
{
    public Task<Result<CreateDemoOAuthClientResponse>> CreateClientAsync(CreateDemoOAuthClientRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<CreateDemoOAuthClientRequest, Result<CreateDemoOAuthClientResponse>>(HttpMethod.Post, "./rpc/OAuthChainDemo/CreateClient", request, context, cancellationToken);

    public Task<Result<DemoTokenResponse>> GetClientTokenAsync(GetDemoClientTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<GetDemoClientTokenRequest, Result<DemoTokenResponse>>(HttpMethod.Post, "./rpc/OAuthChainDemo/GetClientToken", request, context, cancellationToken);

    public Task<Result<DemoTokenResponse>> GetUserTokenAsync(GetDemoUserTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<GetDemoUserTokenRequest, Result<DemoTokenResponse>>(HttpMethod.Post, "./rpc/OAuthChainDemo/GetUserToken", request, context, cancellationToken);

    public Task<Result<DemoTokenResponse>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<ExchangeDemoTokenRequest, Result<DemoTokenResponse>>(HttpMethod.Post, "./rpc/OAuthChainDemo/ExchangeToken", request, context, cancellationToken);

    public Task<Result<ConsumeDemoAccessTokenResponse>> CallDownstreamAsync(CallDemoDownstreamRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<CallDemoDownstreamRequest, Result<ConsumeDemoAccessTokenResponse>>(HttpMethod.Post, "./rpc/OAuthChainDemo/CallDownstream", request, context, cancellationToken);
}
