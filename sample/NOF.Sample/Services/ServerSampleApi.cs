using NOF.Contract;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerSampleApi(IServiceProvider services) : ISampleApi
{
    public Task<Result> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.CreateConfigNode.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.DeleteConfigNode.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.AddOrUpdateConfigFile.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.RemoveConfigFile.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.SetActiveFile.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.UpdateConfigNodeParent.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.GetConfigNodeChildren.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.GetConfigNodeById.InvokeAsync(services, request, context, cancellationToken);

    public Task<GetRootConfigNodesResponse> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.GetRootConfigNodes.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.GetConfiguration.InvokeAsync(services, request, context, cancellationToken);

    public Task<Result<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.WatchSampleStream.InvokeAsync(services, request, context, cancellationToken);
}
