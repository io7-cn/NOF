# 配置节点 REST 完整实现

[English](../../en/design/configuration-rest.md) · [HTTP 绑定规则](http-adaptation.md)

Contract 声明成功值与 FailureCase，Application 实现生成的操作 Override。HTTP 适配器是一个普通静态功能扩展；宿主只注册一次 Application 程序集/服务容器，再调用 MapConfigNodeEndpoints。不注册端点模块服务，不在宿主逐 UseCase 注册 Handler。

## 完整端点适配器

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

## 契约、Handler 与宿主

INOFSampleService 声明 CreateConfigNode、GetConfigNodeById、GetRootConfigNodes、UpdateConfigNodeParent、DeleteConfigNode，以及文件/配置/SSE 操作。Create 返回 CreatedConfigNode；FailureCase 区分 NameConflict、ParentNotFound、InvalidInput、NotFound、ParentCycle、HasChildren，后者携带 NodeHasChildrenError。全部分支通过 IFailure 提供 Title/Detail，HTTP 状态按生成的具体类型选择。

NOFSampleService 继承 ApplicationService<INOFSampleService>。Create 校验值对象/父级、保存聚合并返回持久化 ID；Move 拒绝缺失父级和循环；Delete 拒绝有子节点的删除。领域事件、缓存和读取投影仍在 Application/Domain，Handler 不引用 ASP.NET Core。

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

PostgreSQL/Redis/RabbitMQ 由宿主独立选择。租户意图只从显式配置的 Header 读取，再由调用边界验证；缓存前缀隔离租户。Move 的 parentId 为必需可空字段：缺失无效，null 表示移到根，路由 id 不从 Body 读取。Empty 默认 204，Create 返回 201 和 Location；具体未找到/冲突/无效输入分支对应 404/409/400，其他声明失败默认 422。SSE 使用原生 Writer 和显式事件契约。

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

Title 是展示值，不作判别。不输出 code/message 别名，不自动铺开 Error，不套 Result 信封。UI 消费原生 HTTP 状态和 Problem title/detail；本地 ServerSampleApi 将 IFailure 转为 UI 自有端口结果，需要按状态决策的远程适配器保存真实 HttpStatusCode。

## 可执行源码

- [Contract](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.Contract/INOFSampleService.cs)
- [Application Handler](https://github.com/io7-cn/NOF/tree/main/sample/NOF.Sample.Application/RequestHandlers)
- [完整端点适配器](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Endpoints/ConfigNodeEndpoints.cs)
- [宿主](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Program.cs)
- [集成测试](https://github.com/io7-cn/NOF/blob/main/sample-tests/NOF.Sample.Tests/ConfigNodeRestTests.cs)

测试覆盖真实 CRUD/持久化、Location 回读、投影、循环、文件、无效输入、租户隔离、UI、策略拒绝、OpenAPI 以及旧 RPC 路由不可用。关闭反射的 HTTP 冒烟测试也覆盖泛型操作 API。
