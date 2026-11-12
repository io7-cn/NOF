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

    public static async Task<Results<CreatedAtRoute<CreatedConfigNode>, ProblemHttpResult>> CreateAsync(
        [FromBody] CreateConfigNodeBody body, HttpContext http, CancellationToken ct)
    {
        var result = await http.InvokeAsync(NOFSampleService.CreateConfigNode.Descriptor,
            new CreateConfigNodeRequest { Name = body.Name, ParentId = body.ParentId }, ct);
        return result.Value switch
        {
            CreatedConfigNode node => TypedResults.CreatedAtRoute(node, GetNodeRoute, new { id = node.Id }),
            Failure failure => ToProblem(failure),
            _ => throw new InvalidOperationException("Create did not return a business outcome.")
        };
    }

    public static async Task<Ok<List<ConfigNodeDto>>> GetRootsAsync(HttpContext http, CancellationToken ct)
    {
        var response = await http.InvokeAsync(NOFSampleService.GetRootConfigNodes.Descriptor, new GetRootConfigNodesRequest(), ct);
        return TypedResults.Ok(response.Nodes);
    }

    public static async Task<Results<Ok<ConfigNodeDto>, ProblemHttpResult>> GetAsync(long id, HttpContext http, CancellationToken ct)
    {
        var result = await http.InvokeAsync(NOFSampleService.GetConfigNodeById.Descriptor, new GetConfigNodeByIdRequest { Id = id }, ct);
        return result.Value switch
        {
            GetConfigNodeByIdResponse found => TypedResults.Ok(found.Node),
            Failure failure => ToProblem(failure),
            _ => throw new InvalidOperationException("Get did not return a business outcome.")
        };
    }

    public static async Task<Results<Ok<GetConfigNodeChildrenResponse>, ProblemHttpResult>> GetChildrenAsync(
        long id, HttpContext http, CancellationToken ct)
    {
        var result = await http.InvokeAsync(NOFSampleService.GetConfigNodeChildren.Descriptor,
            new GetConfigNodeChildrenRequest { Id = id }, ct);
        return result.Value switch
        {
            GetConfigNodeChildrenResponse children => TypedResults.Ok(children),
            Failure failure => ToProblem(failure),
            _ => throw new InvalidOperationException("Children query did not return a business outcome.")
        };
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> MoveAsync(
        long id, [FromBody] MoveConfigNodeBody body, HttpContext http, CancellationToken ct)
        => ToCommandResult(await http.InvokeAsync(NOFSampleService.UpdateConfigNodeParent.Descriptor,
            new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }, ct));

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(long id, HttpContext http, CancellationToken ct)
        => ToCommandResult(await http.InvokeAsync(NOFSampleService.DeleteConfigNode.Descriptor, new DeleteConfigNodeRequest { Id = id }, ct));

    public static async Task<Results<NoContent, ProblemHttpResult>> PutFileAsync(
        long id, string fileName, [FromBody] ConfigFileBody body, HttpContext http, CancellationToken ct)
        => ToCommandResult(await http.InvokeAsync(NOFSampleService.AddOrUpdateConfigFile.Descriptor,
            new AddOrUpdateConfigFileRequest { NodeId = id, FileName = fileName, Content = body.Content }, ct));

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteFileAsync(
        long id, string fileName, HttpContext http, CancellationToken ct)
        => ToCommandResult(await http.InvokeAsync(NOFSampleService.RemoveConfigFile.Descriptor,
            new RemoveConfigFileRequest { NodeId = id, FileName = fileName }, ct));

    public static async Task<Results<NoContent, ProblemHttpResult>> SetActiveFileAsync(
        long id, [FromBody] ActiveConfigFileBody body, HttpContext http, CancellationToken ct)
        => ToCommandResult(await http.InvokeAsync(NOFSampleService.SetActiveFile.Descriptor,
            new SetActiveFileRequest { NodeId = id, FileName = body.FileName }, ct));

    public static async Task<Results<Ok<GetConfigurationResponse>, ProblemHttpResult>> GetConfigurationAsync(
        string appName, HttpContext http, CancellationToken ct)
    {
        var result = await http.InvokeAsync(NOFSampleService.GetConfiguration.Descriptor,
            new GetConfigurationRequest { AppName = appName }, ct);
        return result.Value switch
        {
            GetConfigurationResponse configuration => TypedResults.Ok(configuration),
            Failure failure => ToProblem(failure),
            _ => throw new InvalidOperationException("Configuration query did not return a business outcome.")
        };
    }

    public static async Task<IResult> WatchAsync(HttpContext http, string? topic, int? count, int? intervalMilliseconds, bool? forceFailure, CancellationToken ct)
    {
        var result = await http.InvokeAsync(NOFSampleService.WatchSampleStream.Descriptor,
            new WatchSampleStreamRequest
            {
                Topic = topic ?? "NOF Streaming Demo",
                Count = count ?? 5,
                IntervalMilliseconds = intervalMilliseconds ?? 1000,
                ForceFailure = forceFailure ?? false
            }, ct);
        return result.Value switch
        {
            IAsyncEnumerable<SampleStreamEvent> events => TypedResults.ServerSentEvents(events),
            Failure failure => ToProblem(failure),
            _ => throw new InvalidOperationException("Stream did not return a business outcome.")
        };
    }

    public static Results<NoContent, ProblemHttpResult> ToCommandResult(Result result) => result.Value switch
    {
        Success => TypedResults.NoContent(),
        Failure failure => ToProblem(failure),
        _ => throw new InvalidOperationException("Command did not return a business outcome.")
    };

    public static ProblemHttpResult ToProblem(Failure failure)
    {
        var status = failure.Code switch
        {
            ConfigNodeErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ConfigNodeErrorCodes.ParentNotFound => StatusCodes.Status422UnprocessableEntity,
            ConfigNodeErrorCodes.NameConflict or ConfigNodeErrorCodes.ParentCycle or ConfigNodeErrorCodes.HasChildren
                => StatusCodes.Status409Conflict,
            _ => int.TryParse(failure.Code, out var code) && code is >= 400 and <= 599 ? code : StatusCodes.Status500InternalServerError
        };
        return TypedResults.Problem(statusCode: status, detail: failure.Message,
            extensions: new Dictionary<string, object?> { ["code"] = failure.Code });
    }
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
