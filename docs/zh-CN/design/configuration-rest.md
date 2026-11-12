# 配置节点 REST 实现

[English](../../en/design/configuration-rest.md) · [应用服务](application-requests.md)

实际示例将业务声明放在 NOF.Sample.Contract，现有生成操作的 override 放在
NOF.Sample.Application，HTTP 适配器放在 NOF.Sample/Endpoints。宿主只配置服务容器和
基础设施，功能模块负责路由、绑定、状态码和响应类型。

## 契约和 Handler

```csharp
public interface INOFSampleService : IApplicationService
{
    Result<CreatedConfigNode> CreateConfigNode(CreateConfigNodeRequest request);
    Result<GetConfigNodeByIdResponse> GetConfigNodeById(GetConfigNodeByIdRequest request);
    GetRootConfigNodesResponse GetRootConfigNodes(GetRootConfigNodesRequest request);
    Result UpdateConfigNodeParent(UpdateConfigNodeParentRequest request);
    Result DeleteConfigNode(DeleteConfigNodeRequest request);
    // 文件、子节点、配置查询和流式操作也是普通业务声明。
}
public sealed record CreatedConfigNode(long Id);
public partial class NOFSampleService : ApplicationService<INOFSampleService>;
```

创建 Handler 校验名称和父节点、保存聚合、更新缓存并返回实际保存的节点 ID。
移动拒绝不存在的父节点、自身作为父节点及后代循环，删除拒绝仍有子节点的节点。
原有领域事件和投影 Handler 继续运行，业务 Handler 不引用 ASP.NET Core。

## HTTP 模块

ConfigNodeEndpoints 实现 IHttpEndpointModule，沿用 AutoInject 程序集初始化器注册：

| HTTP | 路径 | 输入绑定 | 成功响应 |
| --- | --- | --- | --- |
| POST | /api/config-nodes | body: name、可选 parentId | 201 CreatedConfigNode，Location 指向 GET |
| GET | /api/config-nodes | 无 | 200 根节点 ConfigNodeDto 数组 |
| GET | /api/config-nodes/{id} | path id | 200 ConfigNodeDto |
| GET | /api/config-nodes/{id}/children | path id | 200 子节点投影 |
| PATCH | /api/config-nodes/{id}/parent | path id；body parentId，允许显式 null | 204 |
| DELETE | /api/config-nodes/{id} | path id | 204 |
| PUT | /api/config-nodes/{id}/files/{fileName} | path 身份；body content | 204 |
| DELETE | /api/config-nodes/{id}/files/{fileName} | path 身份 | 204 |
| PUT | /api/config-nodes/{id}/active-file | path id；body fileName，允许显式 null | 204 |
| GET | /api/configuration | query appName | 200 配置内容 |
| GET | /api/sample-events | query topic/count/intervalMilliseconds/forceFailure | SSE |

HTTP Body 类型属于适配器，与业务请求分开。例如 MoveConfigNodeBody 只包含 parentId，
端点使用 path id 和 body parentId 构建业务请求；伪造 body nodeId 不能修改操作目标。
省略 parentId 属于非法输入，显式 null 表示移动到根节点。

移动端点实际使用原生绑定和结果类型：

```csharp
nodes.MapPatch("/{id:long}/parent", MoveAsync);

public static async Task<Results<NoContent, ProblemHttpResult>> MoveAsync(
    long id, [FromBody] MoveConfigNodeBody body, HttpContext http, CancellationToken ct)
    => ToCommandResult(await http.InvokeAsync(NOFSampleService.UpdateConfigNodeParent.Descriptor,
        new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }, ct));
```

业务失败返回 application/problem+json，包含 detail 和 code：节点不存在为 404，父节点不存在为
422，名称冲突、循环父子关系、删除仍有子节点的节点为 409，输入校验为 400。
ServiceRejectionHttpFilter 单独处理策略拒绝，保留 401/403 及 challenge 信息。

```http
HTTP/1.1 201 Created
Location: /api/config-nodes/123
Content-Type: application/json

{"id":123}
```

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,"detail":"Node with same name already exists.","code":"config_node.name_conflict"}
```

## 宿主和调用方

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
// NOF 宿主已添加调用程序集，其中包含 AutoInject 端点模块。
builder.Services.Configure<CacheServiceOptions>(options => options.KeyPrefix = "nof-sample:{tenantId}:");
builder.Services.AddProblemDetails();
var app = await builder.BuildAsync();
app.UseStatusCodePages();
app.MapOpenApi();
```

现有宿主继续配置 PostgreSQL 每租户数据库、Redis 和 RabbitMQ，不逐项注册 CRUD 操作。
独立 HTTP 程序集只需添加一次 ApplicationPart。缓存前缀包含 tenantId，避免缓存的资源跨越
数据库租户边界。

共享 UI 接口由应用自行维护。ServerSampleApi 调用本地操作，HttpSampleApi 显式消费原生
资源响应、201/204 和 ProblemDetails，WASM/MAUI 不再调用旧 /rpc/NOFSample 路径。
OAuth 演示使用自己的端点模块，同时保持原有 URL。

## 实际源码和验证

- [服务契约](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.Contract/INOFSampleService.cs)
- [创建 Handler](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.Application/RequestHandlers/CreateConfigNode.cs)
- [完整端点模块及 HTTP Body](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Endpoints/ConfigNodeEndpoints.cs)
- [宿主注册](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Program.cs)
- [UI HTTP 适配器](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.UI/Services/HttpSampleApi.cs)
- [HTTP 集成测试](https://github.com/io7-cn/NOF/blob/main/sample-tests/NOF.Sample.Tests/ConfigNodeRestTests.cs)

测试覆盖真实 Handler 和持久化、Location 跟随、层级投影、文件 CRUD、冲突和循环、非法 Body、
SQLite 跨租户读取、实际 UI 适配器、Handler 构建前的策略拒绝、OpenAPI，以及旧隐式 RPC 路由
不再暴露。修改 HTTP 桥接时运行示例测试项目和框架 Native AOT 冒烟。
