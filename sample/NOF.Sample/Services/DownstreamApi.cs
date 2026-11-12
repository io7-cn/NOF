using NOF.Contract;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>The sample's explicitly maintained downstream HTTP port.</summary>
public interface IDemoDownstreamApi
{
    Task<IDemoDownstreamService.InspectAccessTokenOperation.Result> InspectAccessTokenAsync(
        Empty request, Context context, CancellationToken cancellationToken = default);
}

/// <summary>Calls the sample downstream endpoint through HttpClient.</summary>
public sealed class DownstreamApi(HttpClient httpClient)
    : SampleHttpApi(httpClient), IDemoDownstreamApi
{
    public async Task<IDemoDownstreamService.InspectAccessTokenOperation.Result> InspectAccessTokenAsync(
        Empty request, Context context, CancellationToken cancellationToken = default)
        => (await SendResourceAsync<Empty, ConsumeDemoAccessTokenResponse>(HttpMethod.Post,
            "./api/downstream/access-token-inspections", request, context, cancellationToken)).Value switch
        {
            ConsumeDemoAccessTokenResponse value => value,
            NOF.Abstraction.Failure { Code: "401" } failure => new IDemoDownstreamService.InspectAccessTokenOperation.Unauthorized(failure.Message),
            NOF.Abstraction.Failure { Code: "403" } failure => new IDemoDownstreamService.InspectAccessTokenOperation.Forbidden(failure.Message),
            NOF.Abstraction.Failure { Code: "500" } failure => new IDemoDownstreamService.InspectAccessTokenOperation.ServerError(failure.Message),
            NOF.Abstraction.Failure failure => new IDemoDownstreamService.InspectAccessTokenOperation.Rejected(failure.Message),
            _ => throw new InvalidOperationException("No downstream response.")
        };
}
