using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Sample.Services;

namespace NOF.Sample.Endpoints;

[AutoInject(ServiceLifetime.Singleton)]
public sealed class OAuthDemoEndpoints : IHttpEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var demo = endpoints.MapGroup("/api/oauth-demo").WithTags("OAuth demo")
            .AddEndpointFilter<ServiceRejectionHttpFilter>();
        demo.MapPost("/clients", CreateClientAsync);
        demo.MapPost("/tokens/client", GetClientTokenAsync);
        demo.MapPost("/tokens/user", GetUserTokenAsync);
        demo.MapPost("/tokens/exchange", ExchangeTokenAsync);
        demo.MapPost("/downstream-inspections", CallDownstreamAsync);
        endpoints.MapPost("/api/downstream/access-token-inspections", InspectAccessTokenAsync)
            .AddEndpointFilter<ServiceRejectionHttpFilter>();
    }

    public static async Task<Results<Ok<CreateDemoOAuthClientResponse>, ProblemHttpResult>> CreateClientAsync(CreateDemoOAuthClientRequest request, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(OAuthChainDemoService.CreateClient.Descriptor, request, ct)) switch
        {
            ProblemHttpResult problem => problem,
            CreateDemoOAuthClientResponse value => TypedResults.Ok(value),
            OAuthChainDemoService.CreateClient.Rejected rejected => ToProblem(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.CreateClient.Unauthorized unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.CreateClient.Forbidden forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.CreateClient.ServerError serverError => ToProblem(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<DemoTokenResponse>, ProblemHttpResult>> GetClientTokenAsync(GetDemoClientTokenRequest request, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(OAuthChainDemoService.GetClientToken.Descriptor, request, ct)) switch
        {
            ProblemHttpResult problem => problem,
            DemoTokenResponse value => TypedResults.Ok(value),
            OAuthChainDemoService.GetClientToken.Rejected rejected => ToProblem(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.GetClientToken.Unauthorized unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.GetClientToken.Forbidden forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.GetClientToken.ServerError serverError => ToProblem(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<DemoTokenResponse>, ProblemHttpResult>> GetUserTokenAsync(GetDemoUserTokenRequest request, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(OAuthChainDemoService.GetUserToken.Descriptor, request, ct)) switch
        {
            ProblemHttpResult problem => problem,
            DemoTokenResponse value => TypedResults.Ok(value),
            OAuthChainDemoService.GetUserToken.Rejected rejected => ToProblem(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.GetUserToken.Unauthorized unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.GetUserToken.Forbidden forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.GetUserToken.ServerError serverError => ToProblem(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<DemoTokenResponse>, ProblemHttpResult>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(OAuthChainDemoService.ExchangeToken.Descriptor, request, ct)) switch
        {
            ProblemHttpResult problem => problem,
            DemoTokenResponse value => TypedResults.Ok(value),
            OAuthChainDemoService.ExchangeToken.Rejected rejected => ToProblem(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.ExchangeToken.Unauthorized unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.ExchangeToken.Forbidden forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.ExchangeToken.ServerError serverError => ToProblem(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<ConsumeDemoAccessTokenResponse>, ProblemHttpResult>> CallDownstreamAsync(CallDemoDownstreamRequest request, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(OAuthChainDemoService.CallDownstream.Descriptor, request, ct)) switch
        {
            ProblemHttpResult problem => problem,
            ConsumeDemoAccessTokenResponse value => TypedResults.Ok(value),
            OAuthChainDemoService.CallDownstream.Rejected rejected => ToProblem(rejected.ErrorCode, rejected.Message),
            OAuthChainDemoService.CallDownstream.Unauthorized unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
            OAuthChainDemoService.CallDownstream.Forbidden forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
            OAuthChainDemoService.CallDownstream.ServerError serverError => ToProblem(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<ConsumeDemoAccessTokenResponse>, ProblemHttpResult>> InspectAccessTokenAsync(Empty request, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(DemoDownstreamService.InspectAccessToken.Descriptor, request, ct)) switch
        {
            ProblemHttpResult problem => problem,
            ConsumeDemoAccessTokenResponse value => TypedResults.Ok(value),
            DemoDownstreamService.InspectAccessToken.Rejected rejected => ToProblem(rejected.ErrorCode, rejected.Message),
            DemoDownstreamService.InspectAccessToken.Unauthorized unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
            DemoDownstreamService.InspectAccessToken.Forbidden forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
            DemoDownstreamService.InspectAccessToken.ServerError serverError => ToProblem(serverError.ErrorCode, serverError.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    private static ProblemHttpResult ToProblem(string errorCode, string message) => TypedResults.Problem(
        statusCode: int.TryParse(errorCode, out var code) && code is >= 400 and <= 599 ? code : 400,
        detail: message, extensions: new Dictionary<string, object?> { ["code"] = errorCode });
}
