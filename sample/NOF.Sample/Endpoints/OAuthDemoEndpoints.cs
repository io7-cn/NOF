using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Services;

namespace NOF.Sample.Endpoints;

public static class OAuthDemoEndpoints
{
    public static void MapOAuthDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var demo = endpoints.MapGroup("/api/oauth-demo").WithTags("OAuth demo");
        demo.MapPost<OAuthChainDemoService.CreateClient>("/clients")
            .Response<OAuthChainDemoService.CreateClient.Rejected>(400)
            .Response<OAuthChainDemoService.CreateClient.Unauthorized>(401)
            .Response<OAuthChainDemoService.CreateClient.Forbidden>(403)
            .Response<OAuthChainDemoService.CreateClient.ServerError>(500);
        demo.MapPost<OAuthChainDemoService.GetClientToken>("/tokens/client")
            .Response<OAuthChainDemoService.GetClientToken.Rejected>(400)
            .Response<OAuthChainDemoService.GetClientToken.Unauthorized>(401)
            .Response<OAuthChainDemoService.GetClientToken.Forbidden>(403)
            .Response<OAuthChainDemoService.GetClientToken.ServerError>(500);
        demo.MapPost<OAuthChainDemoService.GetUserToken>("/tokens/user")
            .Response<OAuthChainDemoService.GetUserToken.Rejected>(400)
            .Response<OAuthChainDemoService.GetUserToken.Unauthorized>(401)
            .Response<OAuthChainDemoService.GetUserToken.Forbidden>(403)
            .Response<OAuthChainDemoService.GetUserToken.ServerError>(500);
        demo.MapPost<OAuthChainDemoService.ExchangeToken>("/tokens/exchange")
            .Response<OAuthChainDemoService.ExchangeToken.Rejected>(400)
            .Response<OAuthChainDemoService.ExchangeToken.Unauthorized>(401)
            .Response<OAuthChainDemoService.ExchangeToken.Forbidden>(403)
            .Response<OAuthChainDemoService.ExchangeToken.ServerError>(500);
        demo.MapPost<OAuthChainDemoService.CallDownstream>("/downstream-inspections")
            .Response<OAuthChainDemoService.CallDownstream.Rejected>(400)
            .Response<OAuthChainDemoService.CallDownstream.Unauthorized>(401)
            .Response<OAuthChainDemoService.CallDownstream.Forbidden>(403)
            .Response<OAuthChainDemoService.CallDownstream.ServerError>(500);
        endpoints.MapPost<DemoDownstreamService.InspectAccessToken>("/api/downstream/access-token-inspections")
            .RequireAuthorization()
            .Response<DemoDownstreamService.InspectAccessToken.Rejected>(400)
            .Response<DemoDownstreamService.InspectAccessToken.Unauthorized>(401)
            .Response<DemoDownstreamService.InspectAccessToken.Forbidden>(403)
            .Response<DemoDownstreamService.InspectAccessToken.ServerError>(500);
    }
}
