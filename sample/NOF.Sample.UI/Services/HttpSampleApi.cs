using NOF.Contract;

namespace NOF.Sample.UI.Services;

/// <summary>Explicit HTTP integration owned by the sample UI.</summary>
public sealed class HttpSampleApi(HttpClient httpClient)
    : SampleHttpApi(httpClient), ISampleApi
{
    public Task<Result> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<CreateConfigNodeRequest, Result>(HttpMethod.Post, "./rpc/NOFSample/CreateConfigNode", request, context, cancellationToken);

    public Task<Result> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<DeleteConfigNodeRequest, Result>(HttpMethod.Delete, Query("./rpc/NOFSample/DeleteConfigNode", ("Id", request.Id)), request, context, cancellationToken);

    public Task<Result> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<AddOrUpdateConfigFileRequest, Result>(HttpMethod.Put, "./rpc/NOFSample/AddOrUpdateConfigFile", request, context, cancellationToken);

    public Task<Result> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<RemoveConfigFileRequest, Result>(HttpMethod.Delete, Query("./rpc/NOFSample/RemoveConfigFile", ("NodeId", request.NodeId), ("FileName", request.FileName)), request, context, cancellationToken);

    public Task<Result> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<SetActiveFileRequest, Result>(HttpMethod.Patch, "./rpc/NOFSample/SetActiveFile", request, context, cancellationToken);

    public Task<Result> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<UpdateConfigNodeParentRequest, Result>(HttpMethod.Patch, "./rpc/NOFSample/UpdateConfigNodeParent", request, context, cancellationToken);

    public Task<Result<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<GetConfigNodeChildrenRequest, Result<GetConfigNodeChildrenResponse>>(HttpMethod.Get, Query("./rpc/NOFSample/GetConfigNodeChildren", ("Id", request.Id)), request, context, cancellationToken);

    public Task<Result<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<GetConfigNodeByIdRequest, Result<GetConfigNodeByIdResponse>>(HttpMethod.Get, Query("./rpc/NOFSample/GetConfigNodeById", ("Id", request.Id)), request, context, cancellationToken);

    public Task<GetRootConfigNodesResponse> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => SendValueAsync<GetRootConfigNodesRequest, GetRootConfigNodesResponse>(HttpMethod.Get, "./rpc/NOFSample/GetRootConfigNodes", request, context, cancellationToken);

    public Task<Result<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => SendOutcomeAsync<GetConfigurationRequest, Result<GetConfigurationResponse>>(HttpMethod.Get, Query("./rpc/NOFSample/GetConfiguration", ("AppName", request.AppName)), request, context, cancellationToken);

    public Task<Result<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => SendStreamAsync<WatchSampleStreamRequest, SampleStreamEvent>(HttpMethod.Get, Query("./rpc/NOFSample/WatchSampleStream", ("Topic", request.Topic), ("Count", request.Count), ("IntervalMilliseconds", request.IntervalMilliseconds), ("ForceFailure", request.ForceFailure)), request, context, cancellationToken);
}
