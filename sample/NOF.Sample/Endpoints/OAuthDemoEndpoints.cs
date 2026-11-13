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
            .Response<OAuthChainDemoService.CreateClient.Rejected>(output => output.StatusCode(400))
            .Response<OAuthChainDemoService.CreateClient.Unauthorized>(output => output.StatusCode(401))
            .Response<OAuthChainDemoService.CreateClient.Forbidden>(output => output.StatusCode(403))
            .Response<OAuthChainDemoService.CreateClient.ServerError>(output => output.StatusCode(500));
        demo.MapPost<OAuthChainDemoService.GetClientToken>("/tokens/client")
            .Response<OAuthChainDemoService.GetClientToken.Rejected>(output => output.StatusCode(400))
            .Response<OAuthChainDemoService.GetClientToken.Unauthorized>(output => output.StatusCode(401))
            .Response<OAuthChainDemoService.GetClientToken.Forbidden>(output => output.StatusCode(403))
            .Response<OAuthChainDemoService.GetClientToken.ServerError>(output => output.StatusCode(500));
        demo.MapPost<OAuthChainDemoService.GetUserToken>("/tokens/user")
            .Response<OAuthChainDemoService.GetUserToken.Rejected>(output => output.StatusCode(400))
            .Response<OAuthChainDemoService.GetUserToken.Unauthorized>(output => output.StatusCode(401))
            .Response<OAuthChainDemoService.GetUserToken.Forbidden>(output => output.StatusCode(403))
            .Response<OAuthChainDemoService.GetUserToken.ServerError>(output => output.StatusCode(500));
        demo.MapPost<OAuthChainDemoService.ExchangeToken>("/tokens/exchange")
            .Response<OAuthChainDemoService.ExchangeToken.Rejected>(output => output.StatusCode(400))
            .Response<OAuthChainDemoService.ExchangeToken.Unauthorized>(output => output.StatusCode(401))
            .Response<OAuthChainDemoService.ExchangeToken.Forbidden>(output => output.StatusCode(403))
            .Response<OAuthChainDemoService.ExchangeToken.ServerError>(output => output.StatusCode(500));
        demo.MapPost<OAuthChainDemoService.CallDownstream>("/downstream-inspections")
            .Response<OAuthChainDemoService.CallDownstream.Rejected>(output => output.StatusCode(400))
            .Response<OAuthChainDemoService.CallDownstream.Unauthorized>(output => output.StatusCode(401))
            .Response<OAuthChainDemoService.CallDownstream.Forbidden>(output => output.StatusCode(403))
            .Response<OAuthChainDemoService.CallDownstream.ServerError>(output => output.StatusCode(500));
        endpoints.MapPost<DemoDownstreamService.InspectAccessToken>("/api/downstream/access-token-inspections")
            .Response<DemoDownstreamService.InspectAccessToken.Rejected>(output => output.StatusCode(400))
            .Response<DemoDownstreamService.InspectAccessToken.Unauthorized>(output => output.StatusCode(401))
            .Response<DemoDownstreamService.InspectAccessToken.Forbidden>(output => output.StatusCode(403))
            .Response<DemoDownstreamService.InspectAccessToken.ServerError>(output => output.StatusCode(500));
    }
}
