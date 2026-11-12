using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Sample.UI.Services;

/// <summary>Application-owned integration with the sample REST API.</summary>
public sealed class HttpSampleApi(HttpClient httpClient) : SampleHttpApi(httpClient), ISampleApi
{
    public Task<UiResponse> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => SendCommandAsync(HttpMethod.Post, "./api/config-nodes", request, context, cancellationToken);

    public Task<UiResponse> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => SendCommandAsync(HttpMethod.Delete, $"./api/config-nodes/{request.Id}", request, context, cancellationToken);

    public Task<UiResponse> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => SendCommandAsync(HttpMethod.Put, $"./api/config-nodes/{request.NodeId}/files/{Uri.EscapeDataString(request.FileName)}",
            new FileBody(request.Content), context, cancellationToken);

    public Task<UiResponse> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => SendCommandAsync(HttpMethod.Delete, $"./api/config-nodes/{request.NodeId}/files/{Uri.EscapeDataString(request.FileName)}",
            request, context, cancellationToken);

    public Task<UiResponse> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => SendCommandAsync(HttpMethod.Put, $"./api/config-nodes/{request.NodeId}/active-file", new ActiveFileBody(request.FileName), context, cancellationToken);

    public Task<UiResponse> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => SendCommandAsync(HttpMethod.Patch, $"./api/config-nodes/{request.NodeId}/parent", new ParentBody(request.NewParentId), context, cancellationToken);

    public Task<UiResponse<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<GetConfigNodeChildrenRequest, GetConfigNodeChildrenResponse>(HttpMethod.Get,
            $"./api/config-nodes/{request.Id}/children", request, context, cancellationToken);

    public async Task<UiResponse<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => (await SendResourceAsync<GetConfigNodeByIdRequest, ConfigNodeDto>(HttpMethod.Get,
            $"./api/config-nodes/{request.Id}", request, context, cancellationToken)).Value switch
        {
            ConfigNodeDto node => new GetConfigNodeByIdResponse { Node = node },
            Failure failure => failure,
            _ => throw new InvalidOperationException("The REST response contained no outcome.")
        };

    public async Task<GetRootConfigNodesResponse> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => new()
        {
            Nodes = await SendValueAsync<GetRootConfigNodesRequest, List<ConfigNodeDto>>(HttpMethod.Get,
            "./api/config-nodes", request, context, cancellationToken)
        };

    public Task<UiResponse<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => SendResourceAsync<GetConfigurationRequest, GetConfigurationResponse>(HttpMethod.Get,
            Query("./api/configuration", ("appName", request.AppName)), request, context, cancellationToken);

    public Task<UiResponse<IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => SendStreamAsync<WatchSampleStreamRequest, SampleStreamEvent>(HttpMethod.Get,
            Query("./api/sample-events", ("topic", request.Topic), ("count", request.Count),
                ("intervalMilliseconds", request.IntervalMilliseconds), ("forceFailure", request.ForceFailure)), request, context, cancellationToken);

    public sealed record FileBody(string Content);
    public sealed record ParentBody(long? ParentId);
    public sealed record ActiveFileBody(string? FileName);
}
