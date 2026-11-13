# Configuration node REST implementation

[简体中文](../../zh-CN/design/configuration-rest.md) · [HTTP binding rules](http-adaptation.md)

Contract declares successful values and FailureCase branches; Application implements generated operation overrides. The HTTP adapter is one ordinary static feature extension. Host composition registers the Application assembly/container once and calls MapConfigNodeEndpoints. There is no endpoint-module service registration or per-use-case handler registration in the host.

## Complete endpoint adapter

```csharp
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
```

## Contract, handlers and host

INOFSampleService declares CreateConfigNode, GetConfigNodeById, GetRootConfigNodes, UpdateConfigNodeParent and DeleteConfigNode, alongside file/configuration/SSE operations. Create returns CreatedConfigNode. FailureCase names distinguish NameConflict, ParentNotFound, InvalidInput, NotFound, ParentCycle and HasChildren; HasChildren carries NodeHasChildrenError. Every branch supplies Title/Detail through IFailure. HTTP status selection always uses the concrete generated type.

NOFSampleService derives from ApplicationService<INOFSampleService>. CreateConfigNode validates the value object/parent, saves its aggregate and returns the persisted ID. UpdateConfigNodeParent rejects missing parents and cycles. DeleteConfigNode rejects children. Domain events, caching and read projections remain in Application/Domain; none of these handlers references ASP.NET Core.

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
var app = builder.Build();
app.UseNOF();
app.MapConfigNodeEndpoints();
app.MapOAuthDemoEndpoints();
app.MapOidcServer();
app.MapHealthChecks("/health");
app.MapOpenApi();
```

The sample host chooses PostgreSQL/Redis/RabbitMQ providers separately. Tenant intent is explicitly read from the configured header and validated by the invocation boundary. Tenant cache prefixes preserve isolation. The Move body requires nullable parentId: omitted is invalid, null moves to root; route id is never taken from body. Default Empty writes 204, Create writes 201 and Location, typed missing/conflict/input cases select 404/409/400, other declared failures default to 422. SSE uses the native writer with an explicit event contract.

```http
HTTP/1.1 201 Created
Location: /api/config-nodes/123
Content-Type: application/json

{"id":123}
```

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{"type":"about:blank","title":"config_node.name_conflict","status":409,"detail":"Node with same name already exists."}
```

Title is a display value, never a discriminator. No code/message aliases, automatic Error flattening or mandatory Result envelope are emitted. UI consumers read native HTTP status and Problem title/detail. Local ServerSampleApi maps IFailure to its application-owned UI port; remote adapters keep actual HttpStatusCode where status-specific behavior is required.

## Executable sources

- [Contract](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.Contract/INOFSampleService.cs)
- [Application handlers](https://github.com/io7-cn/NOF/tree/main/sample/NOF.Sample.Application/RequestHandlers)
- [Complete endpoint adapter](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Endpoints/ConfigNodeEndpoints.cs)
- [Host](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Program.cs)
- [Integration tests](https://github.com/io7-cn/NOF/blob/main/sample-tests/NOF.Sample.Tests/ConfigNodeRestTests.cs)

Tests cover actual CRUD/persistence, Location follow-up, projections, cycles, files, malformed inputs, tenant isolation, UI integration, policy rejection, OpenAPI and absence of legacy RPC routes. The reflection-disabled HTTP smoke also exercises the generic operation API.
