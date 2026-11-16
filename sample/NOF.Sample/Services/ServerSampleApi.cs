using NOF.Abstraction;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the UI port to validated server operations.</summary>
/// <remarks>Business execution uses the thread pool rather than the Blazor circuit's synchronization context.
/// The sample domain publishes synchronous events whose handlers can perform asynchronous database work.</remarks>
public sealed class ServerSampleApi(IServiceProvider services) : ISampleApi
{
    public Task<UiResponse> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(Task.Run(() => NOFSampleService.CreateConfigNode.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(Task.Run(() => NOFSampleService.DeleteConfigNode.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(Task.Run(() => NOFSampleService.AddOrUpdateConfigFile.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(Task.Run(() => NOFSampleService.RemoveConfigFile.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(Task.Run(() => NOFSampleService.SetActiveFile.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Command(Task.Run(() => NOFSampleService.UpdateConfigNodeParent.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetConfigNodeChildrenResponse>(Task.Run(() => NOFSampleService.GetConfigNodeChildren.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetConfigNodeByIdResponse>(Task.Run(() => NOFSampleService.GetConfigNodeById.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse<GetRootConfigNodesResponse>> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetRootConfigNodesResponse>(Task.Run(() => NOFSampleService.GetRootConfigNodes.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<GetConfigurationResponse>(Task.Run(() => NOFSampleService.GetConfiguration.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
    public Task<UiResponse<IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => ServerFailureMapping.Resource<IAsyncEnumerable<SampleStreamEvent>>(Task.Run(() => NOFSampleService.WatchSampleStream.InvokeAsync(services, request, context, cancellationToken), cancellationToken));
}
