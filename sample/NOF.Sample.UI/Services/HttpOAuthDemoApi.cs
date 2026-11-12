using NOF.Contract;

namespace NOF.Sample.UI.Services;

/// <summary>Explicit HTTP integration owned by the sample UI.</summary>
public sealed class HttpOAuthDemoApi(HttpClient httpClient)
    : SampleHttpApi(httpClient), IOAuthDemoApi
{
    public Task<UiResponse<CreateDemoOAuthClientResponse>> CreateClientAsync(CreateDemoOAuthClientRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<CreateDemoOAuthClientRequest, CreateDemoOAuthClientResponse>(HttpMethod.Post, "./api/oauth-demo/clients", request, context, cancellationToken);

    public Task<UiResponse<DemoTokenResponse>> GetClientTokenAsync(GetDemoClientTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<GetDemoClientTokenRequest, DemoTokenResponse>(HttpMethod.Post, "./api/oauth-demo/tokens/client", request, context, cancellationToken);

    public Task<UiResponse<DemoTokenResponse>> GetUserTokenAsync(GetDemoUserTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<GetDemoUserTokenRequest, DemoTokenResponse>(HttpMethod.Post, "./api/oauth-demo/tokens/user", request, context, cancellationToken);

    public Task<UiResponse<DemoTokenResponse>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<ExchangeDemoTokenRequest, DemoTokenResponse>(HttpMethod.Post, "./api/oauth-demo/tokens/exchange", request, context, cancellationToken);

    public Task<UiResponse<ConsumeDemoAccessTokenResponse>> CallDownstreamAsync(CallDemoDownstreamRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<CallDemoDownstreamRequest, ConsumeDemoAccessTokenResponse>(HttpMethod.Post, "./api/oauth-demo/downstream-inspections", request, context, cancellationToken);
}
