using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Services;

namespace NOF.Sample.Endpoints;

[AutoInject(ServiceLifetime.Singleton)]
public sealed class OAuthDemoEndpoints : IHttpEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var demo = endpoints.MapGroup("/rpc/OAuthChainDemo").WithTags("OAuth demo")
            .AddEndpointFilter<ServiceRejectionHttpFilter>();
        demo.MapPost("/CreateClient", CreateClientAsync);
        demo.MapPost("/GetClientToken", GetClientTokenAsync);
        demo.MapPost("/GetUserToken", GetUserTokenAsync);
        demo.MapPost("/ExchangeToken", ExchangeTokenAsync);
        demo.MapPost("/CallDownstream", CallDownstreamAsync);
        demo.MapPost("/Downstream/Inspect", InspectAccessTokenAsync);
    }

    public static async Task<Ok<Result<CreateDemoOAuthClientResponse>>> CreateClientAsync(CreateDemoOAuthClientRequest request, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(OAuthChainDemoService.CreateClient.Descriptor, request, ct));

    public static async Task<Ok<Result<DemoTokenResponse>>> GetClientTokenAsync(GetDemoClientTokenRequest request, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(OAuthChainDemoService.GetClientToken.Descriptor, request, ct));

    public static async Task<Ok<Result<DemoTokenResponse>>> GetUserTokenAsync(GetDemoUserTokenRequest request, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(OAuthChainDemoService.GetUserToken.Descriptor, request, ct));

    public static async Task<Ok<Result<DemoTokenResponse>>> ExchangeTokenAsync(ExchangeDemoTokenRequest request, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(OAuthChainDemoService.ExchangeToken.Descriptor, request, ct));

    public static async Task<Ok<Result<ConsumeDemoAccessTokenResponse>>> CallDownstreamAsync(CallDemoDownstreamRequest request, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(OAuthChainDemoService.CallDownstream.Descriptor, request, ct));

    public static async Task<Ok<Result<ConsumeDemoAccessTokenResponse>>> InspectAccessTokenAsync(Empty request, HttpContext http, CancellationToken ct)
        => TypedResults.Ok(await http.InvokeAsync(DemoDownstreamService.InspectAccessToken.Descriptor, request, ct));

}
