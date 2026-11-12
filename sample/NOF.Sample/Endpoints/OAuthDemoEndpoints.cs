using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
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
        => (await http.InvokeAsync(OAuthChainDemoService.CreateClient.Descriptor, request, ct))
            .Match<Results<Ok<CreateDemoOAuthClientResponse>, ProblemHttpResult>>(value => TypedResults.Ok(value),
                rejected => ToProblem(rejected.ErrorCode, rejected.Message),
                unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
                serverError => ToProblem(serverError.ErrorCode, serverError.Message));

    public static async Task<Results<Ok<DemoTokenResponse>, ProblemHttpResult>> GetClientTokenAsync(GetDemoClientTokenRequest request, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(OAuthChainDemoService.GetClientToken.Descriptor, request, ct))
            .Match<Results<Ok<DemoTokenResponse>, ProblemHttpResult>>(value => TypedResults.Ok(value),
                rejected => ToProblem(rejected.ErrorCode, rejected.Message),
                unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
                serverError => ToProblem(serverError.ErrorCode, serverError.Message));

    public static async Task<Results<Ok<DemoTokenResponse>, ProblemHttpResult>> GetUserTokenAsync(GetDemoUserTokenRequest request, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(OAuthChainDemoService.GetUserToken.Descriptor, request, ct))
            .Match<Results<Ok<DemoTokenResponse>, ProblemHttpResult>>(value => TypedResults.Ok(value),
                rejected => ToProblem(rejected.ErrorCode, rejected.Message),
                unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
                serverError => ToProblem(serverError.ErrorCode, serverError.Message));

    public static async Task<Results<Ok<DemoTokenResponse>, ProblemHttpResult>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(OAuthChainDemoService.ExchangeToken.Descriptor, request, ct))
            .Match<Results<Ok<DemoTokenResponse>, ProblemHttpResult>>(value => TypedResults.Ok(value),
                rejected => ToProblem(rejected.ErrorCode, rejected.Message),
                unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
                serverError => ToProblem(serverError.ErrorCode, serverError.Message));

    public static async Task<Results<Ok<ConsumeDemoAccessTokenResponse>, ProblemHttpResult>> CallDownstreamAsync(CallDemoDownstreamRequest request, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(OAuthChainDemoService.CallDownstream.Descriptor, request, ct))
            .Match<Results<Ok<ConsumeDemoAccessTokenResponse>, ProblemHttpResult>>(value => TypedResults.Ok(value),
                rejected => ToProblem(rejected.ErrorCode, rejected.Message),
                unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
                serverError => ToProblem(serverError.ErrorCode, serverError.Message));

    public static async Task<Results<Ok<ConsumeDemoAccessTokenResponse>, ProblemHttpResult>> InspectAccessTokenAsync(Empty request, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(DemoDownstreamService.InspectAccessToken.Descriptor, request, ct))
            .Match<Results<Ok<ConsumeDemoAccessTokenResponse>, ProblemHttpResult>>(value => TypedResults.Ok(value),
                rejected => ToProblem(rejected.ErrorCode, rejected.Message),
                unauthorized => ToProblem(unauthorized.ErrorCode, unauthorized.Message),
                forbidden => ToProblem(forbidden.ErrorCode, forbidden.Message),
                serverError => ToProblem(serverError.ErrorCode, serverError.Message));

    private static ProblemHttpResult ToProblem(string errorCode, string message) => TypedResults.Problem(
        statusCode: int.TryParse(errorCode, out var code) && code is >= 400 and <= 599 ? code : 400,
        detail: message, extensions: new Dictionary<string, object?> { ["code"] = errorCode });
}
