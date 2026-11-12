using NOF.Contract;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerSampleApi(IServiceProvider services) : ISampleApi
{
    public async Task<UiResponse> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.CreateConfigNode.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse>(
            value => Empty.Instance,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.DeleteConfigNode.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse>(
            value => Empty.Instance,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure("config_node.has_children", error.Error.Message));

    public async Task<UiResponse> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.AddOrUpdateConfigFile.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse>(
            value => Empty.Instance,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.RemoveConfigFile.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse>(
            value => Empty.Instance,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.SetActiveFile.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse>(
            value => Empty.Instance,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.UpdateConfigNodeParent.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse>(
            value => Empty.Instance,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.GetConfigNodeChildren.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse<GetConfigNodeChildrenResponse>>(
            value => value,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.GetConfigNodeById.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse<GetConfigNodeByIdResponse>>(
            value => value,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public Task<GetRootConfigNodesResponse> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.GetRootConfigNodes.InvokeAsync(services, request, context, cancellationToken);

    public async Task<UiResponse<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.GetConfiguration.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse<GetConfigurationResponse>>(
            value => value,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

    public async Task<UiResponse<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.WatchSampleStream.InvokeAsync(services, request, context, cancellationToken)).Match<UiResponse<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>>(
            value => value,
            error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message));

}
