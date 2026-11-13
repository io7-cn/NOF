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
            .Response<GetRootConfigNodesResponse>(output => output.Body(value => value.Nodes));
        nodes.MapPost<NOFSampleService.CreateConfigNode>("")
            .WithSummary("Create a configuration node")
            .Response<CreatedConfigNode>(output => output.StatusCode(201)
                .Header("Location", (value, context) => context.LinkTo(GetNodeRoute, new { id = value.Id })))
            .Response<NOFSampleService.CreateConfigNode.NameConflict>(output => output.StatusCode(409))
            .Response<NOFSampleService.CreateConfigNode.InvalidInput>(output => output.StatusCode(400));
        nodes.MapGet<NOFSampleService.GetRootConfigNodes>("");
        nodes.MapGet<NOFSampleService.GetConfigNodeById>("/{id:long}")
            .WithName(GetNodeRoute)
            .Response<GetConfigNodeByIdResponse>(output => output.Body(value => value.Node))
            .Response<NOFSampleService.GetConfigNodeById.NotFound>(output => output.StatusCode(404));
        nodes.MapGet<NOFSampleService.GetConfigNodeChildren>("/{id:long}/children")
            .Response<NOFSampleService.GetConfigNodeChildren.NotFound>(output => output.StatusCode(404));
        nodes.MapPatch<NOFSampleService.UpdateConfigNodeParent>("/{id:long}/parent")
            .Request(request => request.Route<long>("id").Body<MoveConfigNodeBody>().Map((id, body) => new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }))
            .Response<NOFSampleService.UpdateConfigNodeParent.NotFound>(output => output.StatusCode(404))
            .Response<NOFSampleService.UpdateConfigNodeParent.ParentCycle>(output => output.StatusCode(409));
        nodes.MapDelete<NOFSampleService.DeleteConfigNode>("/{id:long}")
            .Response<NOFSampleService.DeleteConfigNode.NotFound>(output => output.StatusCode(404))
            .Response<NOFSampleService.DeleteConfigNode.HasChildren>(output => output.StatusCode(409));
        nodes.MapPut<NOFSampleService.AddOrUpdateConfigFile>("/{id:long}/files/{fileName}")
            .Request(request => request.Member(value => value.NodeId).FromRoute("id"))
            .Response<NOFSampleService.AddOrUpdateConfigFile.NotFound>(output => output.StatusCode(404))
            .Response<NOFSampleService.AddOrUpdateConfigFile.InvalidInput>(output => output.StatusCode(400));
        nodes.MapDelete<NOFSampleService.RemoveConfigFile>("/{id:long}/files/{fileName}")
            .Request(request => request.Member(value => value.NodeId).FromRoute("id"))
            .Response<NOFSampleService.RemoveConfigFile.NotFound>(output => output.StatusCode(404))
            .Response<NOFSampleService.RemoveConfigFile.InvalidInput>(output => output.StatusCode(400));
        nodes.MapPut<NOFSampleService.SetActiveFile>("/{id:long}/active-file")
            .Request(request => request.Member(value => value.NodeId).FromRoute("id"))
            .Response<NOFSampleService.SetActiveFile.NotFound>(output => output.StatusCode(404))
            .Response<NOFSampleService.SetActiveFile.InvalidInput>(output => output.StatusCode(400));
        api.MapGet<NOFSampleService.GetConfiguration>("/configuration").WithTags("Configuration")
            .Response<NOFSampleService.GetConfiguration.NotFound>(output => output.StatusCode(404))
            .Response<NOFSampleService.GetConfiguration.InvalidInput>(output => output.StatusCode(400));
        api.MapGet<NOFSampleService.WatchSampleStream>("/sample-events").WithTags("Streaming")
            .Response<IAsyncEnumerable<SampleStreamEvent>>(output => output.Result((events, _) => TypedResults.ServerSentEvents(events), new(typeof(SampleStreamEvent), "text/event-stream")))
            .Response<NOFSampleService.WatchSampleStream.StreamUnavailable>(output => output.StatusCode(400));
        return nodes;
    }
}

public sealed record MoveConfigNodeBody
{
    public required long? ParentId { get; init; }
}
