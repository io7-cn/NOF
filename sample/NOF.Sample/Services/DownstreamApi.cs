using NOF.Abstraction;
using NOF.Contract;
using NOF.Sample.UI.Services;
using System.Net;

namespace NOF.Sample.Services;

/// <summary>The sample's explicitly maintained downstream HTTP port.</summary>
public interface IDemoDownstreamApi
{
    Task<DemoDownstreamService.InspectAccessToken.Result> InspectAccessTokenAsync(
        Empty request, Context context, CancellationToken cancellationToken = default);
}

/// <summary>Calls the sample downstream endpoint through HttpClient.</summary>
public sealed class DownstreamApi(HttpClient httpClient)
    : SampleHttpApi(httpClient), IDemoDownstreamApi
{
    public async Task<DemoDownstreamService.InspectAccessToken.Result> InspectAccessTokenAsync(
        Empty request, Context context, CancellationToken cancellationToken = default)
        => (await SendTransportResourceAsync<Empty, ConsumeDemoAccessTokenResponse>(HttpMethod.Post,
            "./api/downstream/access-token-inspections", request, context, cancellationToken)).Value switch
        {
            ConsumeDemoAccessTokenResponse value => value,
            HttpTransportFailure { StatusCode: HttpStatusCode.Unauthorized } failure => new DemoDownstreamService.InspectAccessToken.Unauthorized(failure.Detail),
            HttpTransportFailure { StatusCode: HttpStatusCode.Forbidden } failure => new DemoDownstreamService.InspectAccessToken.Forbidden(failure.Detail),
            HttpTransportFailure failure when (int)failure.StatusCode >= 500 => new DemoDownstreamService.InspectAccessToken.ServerError(failure.Detail),
            HttpTransportFailure failure => new DemoDownstreamService.InspectAccessToken.Rejected(failure.Detail),
            _ => throw new InvalidOperationException("No downstream response.")
        };
}
