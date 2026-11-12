using NOF.Contract;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>The sample's explicitly maintained downstream HTTP port.</summary>
public interface IDemoDownstreamApi
{
    Task<Result<ConsumeDemoAccessTokenResponse>> InspectAccessTokenAsync(
        Empty request, Context context, CancellationToken cancellationToken = default);
}

/// <summary>Calls the sample downstream endpoint through HttpClient.</summary>
public sealed class DownstreamApi(HttpClient httpClient)
    : SampleHttpApi(httpClient), IDemoDownstreamApi
{
    public Task<Result<ConsumeDemoAccessTokenResponse>> InspectAccessTokenAsync(
        Empty request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<Empty, ConsumeDemoAccessTokenResponse>(HttpMethod.Post,
            "./api/downstream/access-token-inspections", request, context, cancellationToken);
}
