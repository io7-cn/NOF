using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
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
        => (await http.InvokeAsync(NOFSampleService.CreateConfigNode.Descriptor,
            new CreateConfigNodeRequest { Name = body.Name, ParentId = body.ParentId }, ct)).Match<Results<CreatedAtRoute<CreatedConfigNode>, ProblemHttpResult>>(
            node => TypedResults.CreatedAtRoute(node, GetNodeRoute, new { id = node.Id }),
            error => ToProblem(INOFSampleService.CreateConfigNodeResult.NameConflict.Code, error.Error.Message, 409),
            error => ToProblem(INOFSampleService.CreateConfigNodeResult.ParentNotFound.Code, error.Error.Message, 422),
            error => ToProblem(INOFSampleService.CreateConfigNodeResult.InvalidInput.Code, error.Error.Message, 400));

    public static async Task<Results<Ok<ConfigNodeDto>, ProblemHttpResult>> GetAsync(long id, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.GetConfigNodeById.Descriptor,
            new GetConfigNodeByIdRequest { Id = id }, ct)).Match<Results<Ok<ConfigNodeDto>, ProblemHttpResult>>(
            found => TypedResults.Ok(found.Node),
            error => ToProblem(INOFSampleService.GetConfigNodeByIdResult.NotFound.Code, error.Error.Message, 404));

    public static async Task<Results<Ok<GetConfigNodeChildrenResponse>, ProblemHttpResult>> GetChildrenAsync(long id, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.GetConfigNodeChildren.Descriptor,
            new GetConfigNodeChildrenRequest { Id = id }, ct)).Match<Results<Ok<GetConfigNodeChildrenResponse>, ProblemHttpResult>>(
            children => TypedResults.Ok(children),
            error => ToProblem(INOFSampleService.GetConfigNodeChildrenResult.NotFound.Code, error.Error.Message, 404));

    public static async Task<Results<NoContent, ProblemHttpResult>> MoveAsync(long id, [FromBody] MoveConfigNodeBody body, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.UpdateConfigNodeParent.Descriptor,
            new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }, ct)).Match<Results<NoContent, ProblemHttpResult>>(
            _ => TypedResults.NoContent(),
            error => ToProblem(INOFSampleService.UpdateConfigNodeParentResult.NotFound.Code, error.Error.Message, 404),
            error => ToProblem(INOFSampleService.UpdateConfigNodeParentResult.ParentCycle.Code, error.Error.Message, 409),
            error => ToProblem(INOFSampleService.UpdateConfigNodeParentResult.ParentNotFound.Code, error.Error.Message, 422));

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(long id, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.DeleteConfigNode.Descriptor,
            new DeleteConfigNodeRequest { Id = id }, ct)).Match<Results<NoContent, ProblemHttpResult>>(
            _ => TypedResults.NoContent(),
            error => ToProblem(INOFSampleService.DeleteConfigNodeResult.NotFound.Code, error.Error.Message, 404),
            error => ToProblem(INOFSampleService.DeleteConfigNodeResult.HasChildren.Code, error.Error.Message, 409));

    public static async Task<Results<NoContent, ProblemHttpResult>> PutFileAsync(long id, string fileName, [FromBody] ConfigFileBody body, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.AddOrUpdateConfigFile.Descriptor,
            new AddOrUpdateConfigFileRequest { NodeId = id, FileName = fileName, Content = body.Content }, ct)).Match<Results<NoContent, ProblemHttpResult>>(
            _ => TypedResults.NoContent(),
            error => ToProblem(INOFSampleService.AddOrUpdateConfigFileResult.NotFound.Code, error.Error.Message, 404),
            error => ToProblem(INOFSampleService.AddOrUpdateConfigFileResult.InvalidInput.Code, error.Error.Message, 400));

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteFileAsync(long id, string fileName, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.RemoveConfigFile.Descriptor,
            new RemoveConfigFileRequest { NodeId = id, FileName = fileName }, ct)).Match<Results<NoContent, ProblemHttpResult>>(
            _ => TypedResults.NoContent(),
            error => ToProblem(INOFSampleService.RemoveConfigFileResult.NotFound.Code, error.Error.Message, 404),
            error => ToProblem(INOFSampleService.RemoveConfigFileResult.InvalidInput.Code, error.Error.Message, 400));

    public static async Task<Results<NoContent, ProblemHttpResult>> SetActiveFileAsync(long id, [FromBody] ActiveConfigFileBody body, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.SetActiveFile.Descriptor,
            new SetActiveFileRequest { NodeId = id, FileName = body.FileName }, ct)).Match<Results<NoContent, ProblemHttpResult>>(
            _ => TypedResults.NoContent(),
            error => ToProblem(INOFSampleService.SetActiveFileResult.NotFound.Code, error.Error.Message, 404),
            error => ToProblem(INOFSampleService.SetActiveFileResult.InvalidInput.Code, error.Error.Message, 400));

    public static async Task<Results<Ok<GetConfigurationResponse>, ProblemHttpResult>> GetConfigurationAsync(string appName, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.GetConfiguration.Descriptor,
            new GetConfigurationRequest { AppName = appName }, ct)).Match<Results<Ok<GetConfigurationResponse>, ProblemHttpResult>>(
            configuration => TypedResults.Ok(configuration),
            error => ToProblem(INOFSampleService.GetConfigurationResult.NotFound.Code, error.Error.Message, 404),
            error => ToProblem(INOFSampleService.GetConfigurationResult.InvalidInput.Code, error.Error.Message, 400));

    public static async Task<Results<ServerSentEventsResult<SampleStreamEvent>, ProblemHttpResult>> WatchAsync(string? topic, int? count, int? intervalMilliseconds, bool? forceFailure, HttpContext http, CancellationToken ct)
        => (await http.InvokeAsync(NOFSampleService.WatchSampleStream.Descriptor,
            new WatchSampleStreamRequest { Topic = topic ?? "NOF Streaming Demo", Count = count ?? 5, IntervalMilliseconds = intervalMilliseconds ?? 1000, ForceFailure = forceFailure ?? false }, ct)).Match<Results<ServerSentEventsResult<SampleStreamEvent>, ProblemHttpResult>>(
            events => TypedResults.ServerSentEvents(events),
            error => ToProblem(INOFSampleService.WatchSampleStreamResult.StreamUnavailable.Code, error.Error.Message, 400));

    public static async Task<Ok<List<ConfigNodeDto>>> GetRootsAsync(HttpContext http, CancellationToken ct)
    {
        var response = await http.InvokeAsync(NOFSampleService.GetRootConfigNodes.Descriptor, new GetRootConfigNodesRequest(), ct);
        return TypedResults.Ok(response.Nodes);
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
