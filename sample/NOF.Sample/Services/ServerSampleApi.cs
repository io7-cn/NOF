using NOF.Contract;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerSampleApi(IServiceProvider services) : ISampleApi
{
    public async Task<Result> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.CreateConfigNode.InvokeAsync(services, request, context, cancellationToken)).Match<Result>(
            value => Result.Success(),
            error => new NOF.Abstraction.Failure(INOFSampleService.CreateConfigNodeResult.NameConflict.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.CreateConfigNodeResult.ParentNotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.CreateConfigNodeResult.InvalidInput.Code, error.Error.Message));

    public async Task<Result> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.DeleteConfigNode.InvokeAsync(services, request, context, cancellationToken)).Match<Result>(
            value => Result.Success(),
            error => new NOF.Abstraction.Failure(INOFSampleService.DeleteConfigNodeResult.NotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.DeleteConfigNodeResult.HasChildren.Code, error.Error.Message));

    public async Task<Result> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.AddOrUpdateConfigFile.InvokeAsync(services, request, context, cancellationToken)).Match<Result>(
            value => Result.Success(),
            error => new NOF.Abstraction.Failure(INOFSampleService.AddOrUpdateConfigFileResult.NotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.AddOrUpdateConfigFileResult.InvalidInput.Code, error.Error.Message));

    public async Task<Result> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.RemoveConfigFile.InvokeAsync(services, request, context, cancellationToken)).Match<Result>(
            value => Result.Success(),
            error => new NOF.Abstraction.Failure(INOFSampleService.RemoveConfigFileResult.NotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.RemoveConfigFileResult.InvalidInput.Code, error.Error.Message));

    public async Task<Result> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.SetActiveFile.InvokeAsync(services, request, context, cancellationToken)).Match<Result>(
            value => Result.Success(),
            error => new NOF.Abstraction.Failure(INOFSampleService.SetActiveFileResult.NotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.SetActiveFileResult.InvalidInput.Code, error.Error.Message));

    public async Task<Result> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.UpdateConfigNodeParent.InvokeAsync(services, request, context, cancellationToken)).Match<Result>(
            value => Result.Success(),
            error => new NOF.Abstraction.Failure(INOFSampleService.UpdateConfigNodeParentResult.NotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.UpdateConfigNodeParentResult.ParentCycle.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.UpdateConfigNodeParentResult.ParentNotFound.Code, error.Error.Message));

    public async Task<Result<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.GetConfigNodeChildren.InvokeAsync(services, request, context, cancellationToken)).Match<Result<GetConfigNodeChildrenResponse>>(
            value => value,
            error => new NOF.Abstraction.Failure(INOFSampleService.GetConfigNodeChildrenResult.NotFound.Code, error.Error.Message));

    public async Task<Result<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.GetConfigNodeById.InvokeAsync(services, request, context, cancellationToken)).Match<Result<GetConfigNodeByIdResponse>>(
            value => value,
            error => new NOF.Abstraction.Failure(INOFSampleService.GetConfigNodeByIdResult.NotFound.Code, error.Error.Message));

    public Task<GetRootConfigNodesResponse> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => NOFSampleService.GetRootConfigNodes.InvokeAsync(services, request, context, cancellationToken);

    public async Task<Result<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.GetConfiguration.InvokeAsync(services, request, context, cancellationToken)).Match<Result<GetConfigurationResponse>>(
            value => value,
            error => new NOF.Abstraction.Failure(INOFSampleService.GetConfigurationResult.NotFound.Code, error.Error.Message),
            error => new NOF.Abstraction.Failure(INOFSampleService.GetConfigurationResult.InvalidInput.Code, error.Error.Message));

    public async Task<Result<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => (await NOFSampleService.WatchSampleStream.InvokeAsync(services, request, context, cancellationToken)).Match<Result<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>>(
            value => value,
            error => new NOF.Abstraction.Failure(INOFSampleService.WatchSampleStreamResult.StreamUnavailable.Code, error.Error.Message));

}
