# 原生 HTTP 适配

[English](../../en/design/http-adaptation.md) · [完整 CRUD](configuration-rest.md) · [Ambient 绑定](ambient-binding.md)

Contract 保留业务声明和数据，Application 生成操作身份/基类/结果。宿主使用原生 Group 和普通 MapFeatureEndpoints 扩展；AddNOF 提供 HTTP 运行服务，UseNOF 为请求绑定便捷服务。路由、健康检查、OpenAPI 和 OIDC 映射由宿主显式选择，不发现端点模块，不生成 Client。

## 泛型操作与请求

MapGet/MapPost/MapPut/MapPatch/MapDelete<Service.Operation> 通过 IServiceOperationDefinition 的静态 Descriptor/Describe 选择操作，不构造 Handler，不动态实例化泛型。轻量 Hosting 分析器从原始声明/引用元数据提供具体 Request lambda 类型，不消费其他生成器输出。手工目录仍可用描述符重载。NativeBuilder 暴露真实 RouteHandlerBuilder，供只接受原生 Builder 的扩展使用。

输入优先级为显式 Member、同名 Route，然后 GET/HEAD/DELETE 使用 Query，POST/PUT/PATCH 使用 JSON Body。Header 显式配置，来源描述使用 HttpInputLocation。Route/Query 默认 OrdinalIgnoreCase。绑定遵循宿主 JSON 命名/转换器、required/init/构造器、默认值和未知成员规则。数组/列表接受重复值，标量多值无效。合成输入后只反序列化一次，Body id 不能覆盖 Route 身份；OpenAPI Body 只展示实际 Body 成员。无效输入在构造 Handler 前返回 400/415；用户 Hook 技术异常继续传播。

Member(...).FromRoute/FromQuery/FromHeader/FromBody 覆盖单字段。完整 Route/Query/Header/Body 链以 Map 结束，支持最多三个源、一个完整 Body。Body() 接受整段标量/集合 JSON；Custom 显式提供输入、正文和媒体契约；ByConvention 重置完整构造。不执行回调发现输入契约。

## 整条响应映射

```csharp
var nodes = app.MapGroup("/api/config-nodes");
nodes.MapGet<NOFSampleService.GetConfigNodeById>("/{id:long}")
    .WithName("GetConfigNode")
    .Response((GetConfigNodeByIdResponse value) => TypedResults.Ok(value.Node))
    .Response<NOFSampleService.GetConfigNodeById.NotFound>(404);
nodes.MapPost<NOFSampleService.CreateConfigNode>("")
    .Response((CreatedConfigNode value) => TypedResults.CreatedAtRoute(
        value: value, routeName: "GetConfigNode", routeValues: new { id = value.Id }))
    .Response<NOFSampleService.CreateConfigNode.NameConflict>(409);
nodes.MapDelete<NOFSampleService.DeleteConfigNode>("/{id:long}")
    .Response<NOFSampleService.DeleteConfigNode.NotFound>(404)
    .Response<NOFSampleService.DeleteConfigNode.HasChildren>(409);
```

Response<T>(status) 指定固定状态及该类型的默认呈现。Response((T value) => TypedResults.X(...)) 提供一条完整原生结果，也有接收 HttpContext 的重载。原生结果委托返回具体 IResult/IEndpointMetadataProvider 类型；不透明结果、异步准备、复杂条件、文件和 SSE 使用普通 C# 端点。

每个确切结果类型只选一条映射：端点优先，否则最近父组，再否则内置默认。同一节点重复声明，最后一条整体替换；不同结果类型互不影响。晚声明的外层不能覆盖更深组。组上不适用的候选忽略，端点显式配置不可能的分支则构建失败。被排除 Filter 不贡献分支或 Schema。Filter 失败直接按具体类型配置，例如 group.Response<RateLimitFilter.Exceeded>(429)，HTTP 不再区分 Filter 专属类别/视图。

端点 Response<T>(202) 整体替换父级投影/Header，使用 T 的默认正文。不再分别合并状态/正文/Header/Problem，不保留命名变体、Selector、动态状态 Provider、ResponseNull 或响应上下文模型。Header、Problem 字段与 Location 使用完整映射中的原生 TypedResults/HttpContext。异常中间件和 ProblemDetails 服务仍由宿主管理。

默认 DTO 为 200 JSON，Empty 为 204 无正文，允许的 null 为 200 JSON null，声明的 IFailure 为 422 ProblemDetails；内置认证/权限/策略失败按确切类型对应 401/403/400。显式 204/304 不写正文。Failure.Title/Detail 只展示，不根据名称/数字推断状态，不重复输出 code/message，也不自动铺开 Error。技术异常和取消继续传播。

## Context 与运行时文档

Context 来源继续显式配置：已认证租户 Claim、租户意图 Header，以及命名的不可变标量 Item。不批量传 Header，不传可变引用、HttpContext 或服务容器。业务/Filter Context 不回流；原生结果委托接收 HttpContext，不接收最终业务 InvocationContext。普通原生端点可通过 http.InvokeAsync(descriptor, request, explicitContext, ct) 进入相同身份/策略边界。

执行和运行时 OpenAPI 读取同一份冻结选择。已知 TypedResults 通过静态 IEndpointMetadataProvider 提供契约，不执行响应工厂。ProblemHttpResult 等状态由实例决定时，用 .NativeBuilder.ProducesProblem(status) 显式声明；普通端点同样使用原生 .Produces*。不探测函数、switch 或 IL。关闭 JSON 反射时，宿主 Context 注册实际请求、投影和 Problem 类型。原生 union 分支目录由生成操作或手工描述符的明确 Case 目录提供。

## 破坏性迁移

Response<T>(r => r.StatusCode(404)) 改成 Response<T>(404)。Body/Header/Problem DSL 改成一条完整原生结果委托或普通端点。删除 ConfigureProblem、HTTP Filter 视图、命名变体/Selector 和 HttpOutputContext；父级映射整体替换。保留 Request 配置、泛型操作映射、原生中间件和统一调用策略。同步升级 JSON/OpenAPI 元数据和消费端，不保留兼容层。
