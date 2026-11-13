using NOF.Abstraction;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the UI port to validated server operations.</summary>
public sealed class ServerSampleApi(IServiceProvider services) : ISampleApi
{
    public Task<UiResponse> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(NOFSampleService.CreateConfigNode.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(NOFSampleService.DeleteConfigNode.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(NOFSampleService.AddOrUpdateConfigFile.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(NOFSampleService.RemoveConfigFile.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(NOFSampleService.SetActiveFile.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(NOFSampleService.UpdateConfigNodeParent.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetConfigNodeChildrenResponse>(NOFSampleService.GetConfigNodeChildren.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetConfigNodeByIdResponse>(NOFSampleService.GetConfigNodeById.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<GetRootConfigNodesResponse>> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetRootConfigNodesResponse>(NOFSampleService.GetRootConfigNodes.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetConfigurationResponse>(NOFSampleService.GetConfiguration.InvokeAsync(services, request, context, cancellationToken));
    public Task<UiResponse<IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<IAsyncEnumerable<SampleStreamEvent>>(NOFSampleService.WatchSampleStream.InvokeAsync(services, request, context, cancellationToken));
}
