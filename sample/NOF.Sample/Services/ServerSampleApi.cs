using NOF.Abstraction;
using NOF.Contract;
using NOF.Infrastructure;
using NOF.Sample.Application;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Services;

/// <summary>Connects the sample UI port to server operations without generated clients.</summary>
public sealed class ServerSampleApi(IServiceProvider services) : ISampleApi
{
    public async Task<UiResponse> CreateConfigNodeAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.CreateConfigNode.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            CreatedConfigNode value => Empty.Instance,
            NOFSampleService.CreateConfigNode.NameConflict error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.CreateConfigNode.ParentNotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.CreateConfigNode.InvalidInput error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse> DeleteConfigNodeAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.DeleteConfigNode.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            Empty value => Empty.Instance,
            NOFSampleService.DeleteConfigNode.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.DeleteConfigNode.HasChildren error => new NOF.Abstraction.Failure("config_node.has_children", error.Error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse> AddOrUpdateConfigFileAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.AddOrUpdateConfigFile.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            Empty value => Empty.Instance,
            NOFSampleService.AddOrUpdateConfigFile.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.AddOrUpdateConfigFile.InvalidInput error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse> RemoveConfigFileAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.RemoveConfigFile.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            Empty value => Empty.Instance,
            NOFSampleService.RemoveConfigFile.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.RemoveConfigFile.InvalidInput error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse> SetActiveFileAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.SetActiveFile.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            Empty value => Empty.Instance,
            NOFSampleService.SetActiveFile.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.SetActiveFile.InvalidInput error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse> UpdateConfigNodeParentAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.UpdateConfigNodeParent.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            Empty value => Empty.Instance,
            NOFSampleService.UpdateConfigNodeParent.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.UpdateConfigNodeParent.ParentCycle error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.UpdateConfigNodeParent.ParentNotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<GetConfigNodeChildrenResponse>> GetConfigNodeChildrenAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.GetConfigNodeChildren.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            GetConfigNodeChildrenResponse value => value,
            NOFSampleService.GetConfigNodeChildren.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<GetConfigNodeByIdResponse>> GetConfigNodeByIdAsync(GetConfigNodeByIdRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.GetConfigNodeById.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            GetConfigNodeByIdResponse value => value,
            NOFSampleService.GetConfigNodeById.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<GetRootConfigNodesResponse>> GetRootConfigNodesAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.GetRootConfigNodes.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            GetRootConfigNodesResponse roots => roots,
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            _ => throw new InvalidOperationException("Unexpected root query result.")
        };

    public async Task<UiResponse<GetConfigurationResponse>> GetConfigurationAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.GetConfiguration.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            GetConfigurationResponse value => value,
            NOFSampleService.GetConfiguration.NotFound error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            NOFSampleService.GetConfiguration.InvalidInput error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public async Task<UiResponse<global::System.Collections.Generic.IAsyncEnumerable<SampleStreamEvent>>> WatchSampleStreamAsync(WatchSampleStreamRequest request, Context context, CancellationToken cancellationToken = default)
        => InvocationResult.Value(await NOFSampleService.WatchSampleStream.InvokeAsync(services, request, context, cancellationToken)) switch
        {
            IInvocationFailure failure => ServerFailureMapping.From(failure),
            IAsyncEnumerable<SampleStreamEvent> value => value,
            NOFSampleService.WatchSampleStream.StreamUnavailable error => new NOF.Abstraction.Failure(error.ErrorCode, error.Message),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

}
