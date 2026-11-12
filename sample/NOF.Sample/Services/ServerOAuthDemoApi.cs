using NOF.Contract;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerOAuthDemoApi(IServiceProvider services) : IOAuthDemoApi
{
    public async Task<UiResponse<CreateDemoOAuthClientResponse>> CreateClientAsync(CreateDemoOAuthClientRequest request, Context context, CancellationToken cancellationToken = default)
        => (await OAuthChainDemoService.CreateClient.InvokeAsync(services, request, context, cancellationToken))
            .Match<UiResponse<CreateDemoOAuthClientResponse>>(value => value,
                rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
                unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
                serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message));

    public async Task<UiResponse<DemoTokenResponse>> GetClientTokenAsync(GetDemoClientTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => (await OAuthChainDemoService.GetClientToken.InvokeAsync(services, request, context, cancellationToken))
            .Match<UiResponse<DemoTokenResponse>>(value => value,
                rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
                unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
                serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message));

    public async Task<UiResponse<DemoTokenResponse>> GetUserTokenAsync(GetDemoUserTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => (await OAuthChainDemoService.GetUserToken.InvokeAsync(services, request, context, cancellationToken))
            .Match<UiResponse<DemoTokenResponse>>(value => value,
                rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
                unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
                serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message));

    public async Task<UiResponse<DemoTokenResponse>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => (await OAuthChainDemoService.ExchangeToken.InvokeAsync(services, request, context, cancellationToken))
            .Match<UiResponse<DemoTokenResponse>>(value => value,
                rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
                unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
                serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message));

    public async Task<UiResponse<ConsumeDemoAccessTokenResponse>> CallDownstreamAsync(CallDemoDownstreamRequest request, Context context, CancellationToken cancellationToken = default)
        => (await OAuthChainDemoService.CallDownstream.InvokeAsync(services, request, context, cancellationToken))
            .Match<UiResponse<ConsumeDemoAccessTokenResponse>>(value => value,
                rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
                unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
                serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message));
}
