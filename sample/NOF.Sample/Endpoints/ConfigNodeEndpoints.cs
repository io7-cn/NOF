using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Application;

namespace NOF.Sample.Endpoints;

/// <summary>Explicit native routes and declarative HTTP adaptation for configuration use cases.</summary>
public static class ConfigNodeEndpoints
{
    public const string GetNodeRoute = "GetConfigNode";
    public static RouteGroupBuilder MapConfigNodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api").Context(context => context.TenantFromHeader(NOFAbstractionConstants.Transport.Headers.TenantId));
        var nodes = api.MapGroup("/config-nodes").WithTags("Configuration nodes")
            .Response((GetRootConfigNodesResponse value) => TypedResults.Ok(value.Nodes));
        nodes.MapPost<NOFSampleService.CreateConfigNode>("")
            .WithSummary("Create a configuration node")
            .Response((CreatedConfigNode value) => TypedResults.CreatedAtRoute(value: value, routeName: GetNodeRoute, routeValues: new { id = value.Id }))
            .Response<NOFSampleService.CreateConfigNode.NameConflict>(409)
            .Response<NOFSampleService.CreateConfigNode.InvalidInput>(400);
        nodes.MapGet<NOFSampleService.GetRootConfigNodes>("");
        nodes.MapGet<NOFSampleService.GetConfigNodeById>("/{id:long}")
            .WithName(GetNodeRoute)
            .Response((GetConfigNodeByIdResponse value) => TypedResults.Ok(value.Node))
            .Response<NOFSampleService.GetConfigNodeById.NotFound>(404);
        nodes.MapGet<NOFSampleService.GetConfigNodeChildren>("/{id:long}/children")
            .Response<NOFSampleService.GetConfigNodeChildren.NotFound>(404);
        nodes.MapPatch<NOFSampleService.UpdateConfigNodeParent>("/{id:long}/parent")
            .Request(request => request.Route<long>("id").Body<MoveConfigNodeBody>().Map((id, body) => new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }))
            .Response<NOFSampleService.UpdateConfigNodeParent.NotFound>(404)
            .Response<NOFSampleService.UpdateConfigNodeParent.ParentCycle>(409);
        nodes.MapDelete<NOFSampleService.DeleteConfigNode>("/{id:long}")
            .Response<NOFSampleService.DeleteConfigNode.NotFound>(404)
            .Response<NOFSampleService.DeleteConfigNode.HasChildren>(409);
        nodes.MapPut<NOFSampleService.AddOrUpdateConfigFile>("/{id:long}/files/{fileName}")
            .Request(request => request.Member(value => value.NodeId).FromRoute("id"))
            .Response<NOFSampleService.AddOrUpdateConfigFile.NotFound>(404)
            .Response<NOFSampleService.AddOrUpdateConfigFile.InvalidInput>(400);
        nodes.MapDelete<NOFSampleService.RemoveConfigFile>("/{id:long}/files/{fileName}")
            .Request(request => request.Member(value => value.NodeId).FromRoute("id"))
            .Response<NOFSampleService.RemoveConfigFile.NotFound>(404)
            .Response<NOFSampleService.RemoveConfigFile.InvalidInput>(400);
        nodes.MapPut<NOFSampleService.SetActiveFile>("/{id:long}/active-file")
            .Request(request => request.Member(value => value.NodeId).FromRoute("id"))
            .Response<NOFSampleService.SetActiveFile.NotFound>(404)
            .Response<NOFSampleService.SetActiveFile.InvalidInput>(400);
        api.MapGet<NOFSampleService.GetConfiguration>("/configuration").WithTags("Configuration")
            .Response<NOFSampleService.GetConfiguration.NotFound>(404)
            .Response<NOFSampleService.GetConfiguration.InvalidInput>(400);
        api.MapGet("/sample-events", WatchAsync).WithTags("Streaming")
            .Produces<SampleStreamEvent>(contentType: "text/event-stream").ProducesProblem(400);
        return nodes;
    }
    private static async Task<IResult> WatchAsync(HttpContext http, CancellationToken ct,
        string topic = "NOF Streaming Demo", int count = 5, int intervalMilliseconds = 1000, bool forceFailure = false)
    {
        var request = new WatchSampleStreamRequest { Topic = topic, Count = count, IntervalMilliseconds = intervalMilliseconds, ForceFailure = forceFailure };
        var context = Context.Empty.WithTenantId(http.Request.Headers[NOFAbstractionConstants.Transport.Headers.TenantId].ToString());
        return NOF.Infrastructure.InvocationResult.Value(await http.InvokeAsync(NOFSampleService.WatchSampleStream.Descriptor, request, context, ct)) switch
        {
            IAsyncEnumerable<SampleStreamEvent> events => TypedResults.ServerSentEvents(events),
            NOFSampleService.WatchSampleStream.StreamUnavailable failure => TypedResults.Problem(statusCode: 400, type: "about:blank", title: failure.Title, detail: failure.Detail),
            IResult native => native,
            IFailure failure => TypedResults.Problem(statusCode: 422, type: "about:blank", title: failure.Title, detail: failure.Detail),
            _ => throw new InvalidOperationException("Unexpected stream outcome.")
        };
    }

}

public sealed record MoveConfigNodeBody
{
    public required long? ParentId { get; init; }
}
