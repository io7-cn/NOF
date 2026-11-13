using NOF.Abstraction;
using NOF.Contract;
using NOF.Infrastructure;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerOAuthDemoApi(IServiceProvider services) : IOAuthDemoApi
{
    public async Task<UiResponse<CreateDemoOAuthClientResponse>> CreateClientAsync(CreateDemoOAuthClientRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await OAuthChainDemoService.CreateClient.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            CreateDemoOAuthClientResponse value => value,
            OAuthChainDemoService.CreateClient.Rejected rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.CreateClient.Unauthorized unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.CreateClient.Forbidden forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.CreateClient.ServerError serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<DemoTokenResponse>> GetClientTokenAsync(GetDemoClientTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await OAuthChainDemoService.GetClientToken.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            DemoTokenResponse value => value,
            OAuthChainDemoService.GetClientToken.Rejected rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.GetClientToken.Unauthorized unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.GetClientToken.Forbidden forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.GetClientToken.ServerError serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<DemoTokenResponse>> GetUserTokenAsync(GetDemoUserTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await OAuthChainDemoService.GetUserToken.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            DemoTokenResponse value => value,
            OAuthChainDemoService.GetUserToken.Rejected rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.GetUserToken.Unauthorized unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.GetUserToken.Forbidden forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.GetUserToken.ServerError serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<DemoTokenResponse>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await OAuthChainDemoService.ExchangeToken.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            DemoTokenResponse value => value,
            OAuthChainDemoService.ExchangeToken.Rejected rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.ExchangeToken.Unauthorized unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.ExchangeToken.Forbidden forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.ExchangeToken.ServerError serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<ConsumeDemoAccessTokenResponse>> CallDownstreamAsync(CallDemoDownstreamRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await OAuthChainDemoService.CallDownstream.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            ConsumeDemoAccessTokenResponse value => value,
            OAuthChainDemoService.CallDownstream.Rejected rejected => new NOF.Abstraction.Failure(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.CallDownstream.Unauthorized unauthorized => new NOF.Abstraction.Failure(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.CallDownstream.Forbidden forbidden => new NOF.Abstraction.Failure(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.CallDownstream.ServerError serverError => new NOF.Abstraction.Failure(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };
}
