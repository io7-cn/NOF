using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Endpoints;

[AutoInject(ServiceLifetime.Singleton)]
public sealed class ConfigNodeEndpoints : IHttpEndpointModule
{
    public const string GetNodeRoute = "GetConfigNode";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api").AddEndpointFilter<ServiceRejectionHttpFilter>();
        var nodes = api.MapGroup("/config-nodes").WithTags("Configuration nodes");
        nodes.MapPost("", CreateAsync).WithSummary("Create a configuration node")
            .ProducesProblem(400).ProducesProblem(409).ProducesProblem(422);
        nodes.MapGet("", GetRootsAsync).WithSummary("List root configuration nodes");
        nodes.MapGet("/{id:long}", GetAsync).WithName(GetNodeRoute).ProducesProblem(404);
        nodes.MapGet("/{id:long}/children", GetChildrenAsync).ProducesProblem(404);
        nodes.MapPatch("/{id:long}/parent", MoveAsync).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
        nodes.MapDelete("/{id:long}", DeleteAsync).ProducesProblem(404).ProducesProblem(409);
        nodes.MapPut("/{id:long}/files/{fileName}", PutFileAsync).ProducesProblem(400).ProducesProblem(404);
        nodes.MapDelete("/{id:long}/files/{fileName}", DeleteFileAsync).ProducesProblem(404);
        nodes.MapPut("/{id:long}/active-file", SetActiveFileAsync).ProducesProblem(400).ProducesProblem(404);
        api.MapGet("/configuration", GetConfigurationAsync).WithTags("Configuration");
        api.MapGet("/sample-events", WatchAsync).WithTags("Streaming")
            .Produces<SampleStreamEvent>(StatusCodes.Status200OK, "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    public static async Task<Results<CreatedAtRoute<CreatedConfigNode>, ProblemHttpResult>> CreateAsync([FromBody] CreateConfigNodeBody body, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.CreateConfigNode.Descriptor,
            new CreateConfigNodeRequest { Name = body.Name, ParentId = body.ParentId }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            CreatedConfigNode node => TypedResults.CreatedAtRoute(node, GetNodeRoute, new { id = node.Id }),
            NOFSampleService.CreateConfigNode.NameConflict error => ToProblem(error.ErrorCode, error.Message, 409),
            NOFSampleService.CreateConfigNode.ParentNotFound error => ToProblem(error.ErrorCode, error.Message, 422),
            NOFSampleService.CreateConfigNode.InvalidInput error => ToProblem(error.ErrorCode, error.Message, 400),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<ConfigNodeDto>, ProblemHttpResult>> GetAsync(long id, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.GetConfigNodeById.Descriptor,
            new GetConfigNodeByIdRequest { Id = id }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            GetConfigNodeByIdResponse found => TypedResults.Ok(found.Node),
            NOFSampleService.GetConfigNodeById.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<GetConfigNodeChildrenResponse>, ProblemHttpResult>> GetChildrenAsync(long id, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.GetConfigNodeChildren.Descriptor,
            new GetConfigNodeChildrenRequest { Id = id }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            GetConfigNodeChildrenResponse children => TypedResults.Ok(children),
            NOFSampleService.GetConfigNodeChildren.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<NoContent, ProblemHttpResult>> MoveAsync(long id, [FromBody] MoveConfigNodeBody body, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.UpdateConfigNodeParent.Descriptor,
            new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            Empty _ => TypedResults.NoContent(),
            NOFSampleService.UpdateConfigNodeParent.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            NOFSampleService.UpdateConfigNodeParent.ParentCycle error => ToProblem(error.ErrorCode, error.Message, 409),
            NOFSampleService.UpdateConfigNodeParent.ParentNotFound error => ToProblem(error.ErrorCode, error.Message, 422),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(long id, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.DeleteConfigNode.Descriptor,
            new DeleteConfigNodeRequest { Id = id }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            Empty _ => TypedResults.NoContent(),
            NOFSampleService.DeleteConfigNode.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            NOFSampleService.DeleteConfigNode.HasChildren error => ToProblem("config_node.has_children", error.Error.Message, 409),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<NoContent, ProblemHttpResult>> PutFileAsync(long id, string fileName, [FromBody] ConfigFileBody body, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.AddOrUpdateConfigFile.Descriptor,
            new AddOrUpdateConfigFileRequest { NodeId = id, FileName = fileName, Content = body.Content }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            Empty _ => TypedResults.NoContent(),
            NOFSampleService.AddOrUpdateConfigFile.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            NOFSampleService.AddOrUpdateConfigFile.InvalidInput error => ToProblem(error.ErrorCode, error.Message, 400),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteFileAsync(long id, string fileName, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.RemoveConfigFile.Descriptor,
            new RemoveConfigFileRequest { NodeId = id, FileName = fileName }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            Empty _ => TypedResults.NoContent(),
            NOFSampleService.RemoveConfigFile.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            NOFSampleService.RemoveConfigFile.InvalidInput error => ToProblem(error.ErrorCode, error.Message, 400),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<NoContent, ProblemHttpResult>> SetActiveFileAsync(long id, [FromBody] ActiveConfigFileBody body, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.SetActiveFile.Descriptor,
            new SetActiveFileRequest { NodeId = id, FileName = body.FileName }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            Empty _ => TypedResults.NoContent(),
            NOFSampleService.SetActiveFile.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            NOFSampleService.SetActiveFile.InvalidInput error => ToProblem(error.ErrorCode, error.Message, 400),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<GetConfigurationResponse>, ProblemHttpResult>> GetConfigurationAsync(string appName, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.GetConfiguration.Descriptor,
            new GetConfigurationRequest { AppName = appName }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            GetConfigurationResponse configuration => TypedResults.Ok(configuration),
            NOFSampleService.GetConfiguration.NotFound error => ToProblem(error.ErrorCode, error.Message, 404),
            NOFSampleService.GetConfiguration.InvalidInput error => ToProblem(error.ErrorCode, error.Message, 400),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<ServerSentEventsResult<SampleStreamEvent>, ProblemHttpResult>> WatchAsync(string? topic, int? count, int? intervalMilliseconds, bool? forceFailure, HttpContext http, CancellationToken ct)
        => InvocationResult.Value(await http.InvokeAsync(NOFSampleService.WatchSampleStream.Descriptor,
            new WatchSampleStreamRequest { Topic = topic ?? "NOF Streaming Demo", Count = count ?? 5, IntervalMilliseconds = intervalMilliseconds ?? 1000, ForceFailure = forceFailure ?? false }, ct)) switch
        {
            ProblemHttpResult problem => problem,
            IAsyncEnumerable<SampleStreamEvent> events => TypedResults.ServerSentEvents(events),
            NOFSampleService.WatchSampleStream.StreamUnavailable error => ToProblem(error.ErrorCode, error.Message, 400),
            _ => throw new InvalidOperationException("Unmapped invocation result.")
        };

    public static async Task<Results<Ok<List<ConfigNodeDto>>, ProblemHttpResult>> GetRootsAsync(HttpContext http, CancellationToken ct)
    {
        var response = await http.InvokeAsync(NOFSampleService.GetRootConfigNodes.Descriptor, new GetRootConfigNodesRequest(), ct);
        return InvocationResult.Value(response) switch
        {
            GetRootConfigNodesResponse roots => TypedResults.Ok(roots.Nodes),
            ProblemHttpResult problem => problem,
            _ => throw new InvalidOperationException("Unexpected root query result.")
        };
    }

    public static ProblemHttpResult ToProblem(string code, string message, int status)
        => TypedResults.Problem(statusCode: status, detail: message,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record CreateConfigNodeBody
{
    public required string Name { get; init; }
    public long? ParentId { get; init; }
}

public sealed record MoveConfigNodeBody
{
    public required long? ParentId { get; init; }
}

public sealed record ConfigFileBody
{
    public required string Content { get; init; }
}

public sealed record ActiveConfigFileBody
{
    public required string? FileName { get; init; }
}
