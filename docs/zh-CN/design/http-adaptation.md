# 原生 HTTP 适配

[English](../../en/design/http-adaptation.md) · [完整 CRUD](configuration-rest.md) · [预期失败](expected-failures.md)

HTTP 属于宿主。Contract 只声明业务输入、成功输出和 FailureCase；Application 生成操作基类、描述符和闭合结果。ASP.NET Core 负责路由、认证授权中间件、异常处理、ProblemDetails、协议和生命周期；NOF 将选定操作适配到这个原生边界。

## 组装与静态操作类型

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
var app = builder.Build();
app.UseNOF();
app.MapConfigNodeEndpoints();
app.MapOidcServer(); // When the OIDC extension is registered.
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
```

功能端点是返回原生 RouteGroupBuilder 的普通扩展方法，不再注册或发现 IHttpEndpointModule，不提供 MapNOFEndpoints、ForService 或 AddNOFHttp。AddNOF 注册 JSON/OpenAPI/ProblemDetails，路由、健康检查及 OIDC 由宿主显式映射。同一个操作可以映射到多个独立配置的路由。

```csharp
var nodes = endpoints.MapGroup("/api/config-nodes");
nodes.MapPost<NOFSampleService.CreateConfigNode>("")
    .Response<CreatedConfigNode>(output => output.StatusCode(201)
        .Header("Location", (value, context) => context.LinkTo("GetConfigNode", new { id = value.Id })))
    .Response<NOFSampleService.CreateConfigNode.NameConflict>(output => output.StatusCode(409));
nodes.MapGet<NOFSampleService.GetConfigNodeById>("/{id:long}")
    .WithName("GetConfigNode")
    .Response<GetConfigNodeByIdResponse>(output => output.Body(value => value.Node))
    .Response<NOFSampleService.GetConfigNodeById.NotFound>(output => output.StatusCode(404));
nodes.MapPatch<NOFSampleService.UpdateConfigNodeParent>("/{id:long}/parent")
    .Request(request => request.Route<long>("id").Body<MoveConfigNodeBody>()
        .Map((id, body) => new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }))
    .Response<NOFSampleService.UpdateConfigNodeParent.NotFound>(output => output.StatusCode(404))
    .Response<NOFSampleService.UpdateConfigNodeParent.ParentCycle>(output => output.StatusCode(409));
```

生成的操作基类实现 IServiceOperationDefinition 的静态抽象 Descriptor/Describe，泛型约束在编译时拒绝无关类型。静态访问器保持请求类型及裁剪/AOT 元数据，不构造 Handler。Hosting SourceGenerator 只为引用的操作生成具体 Request lambda 扩展，从原始 Contract/Application 声明或引用元数据获取请求类型；不生成 Client、路由，不分析函数体，也不依赖另一个生成器先运行。手工或动态操作目录仍可使用描述符重载。

## 输入绑定

显式 Member 优先，其次同名 Route；剩余 GET/HEAD/DELETE 成员来自 Query，POST/PUT/PATCH 来自 JSON Body。Header 必须显式声明，自定义输入位置使用 HttpInputLocation 枚举。Route/Query 默认 OrdinalIgnoreCase，可通过 Requests 独立配置。命名策略、JsonPropertyName、转换器、required/init、构造器默认值和未知成员处理均遵循宿主 JSON 元数据。重复值可绑定数组或 List<T>；标量多值被拒绝。

约定绑定合成一个 JSON 值后只反序列化一次，不先反序列化整个请求再通过 Setter 修改路由身份。Body 文档只包含实际 Body 成员，Body id 不能覆盖 Path id。输入缺失或格式错误返回 400，Body 媒体类型不正确返回 415，均发生在 Handler 构造之前。

单字段使用 `.Request(input => input.Member(value => value.NodeId).FromRoute("id"))`。完整 Route/Query/Header/Body 源链以强类型 Map 结束，支持最多三个源，整个 Body 只能声明一次。Body() 直接读取整个请求，支持数组和标量 JSON。Custom 接收 HttpContext/取消令牌，必须给出 Body/输入元数据。ByConvention 重置完整构造模式；未经重置不能混合 Member 与完整 Map。不通过执行回调发现契约。

## 响应规则

Response<TSource> 选择具体成功或失败分支。StatusCode、Body、每个 Header、每个 Problem 字段/扩展独立合并。具体 Endpoint 直接规则优先于业务 Group 的具体类型规则，然后是 Filter 默认、通用 IFailure/object 规则和内置默认。同类规则按原生更深 Group 优先，同层重复写只替换对应目标，所以覆盖 Service Header 不会丢失 Filter 的状态与 Retry-After。Filter<TFilter> 只消费其静态失败目录，不启用执行；排除的分支不会进入运行时和 OpenAPI。

默认 DTO 为 200 JSON，Empty 为 204 无正文，允许的 null 为 200 JSON null，声明的 IFailure 为 422 ProblemDetails。内置认证/权限/策略分支按确切类型对应 401/403/400。不解析 Title 文本或数字推断状态；404/409/429 按具体类型显式配置。

Body 投影支持 value 或 value/context 委托；NoBody 显式清空正文，RemoveHeader 清空指定继承头。Header 名忽略大小写，Problem 扩展名保留大小写。动态 StatusCode 必须声明有限 possibleValues，实际值越界则失败；204/304 不能配置正文。具名 Response<T>("ready", ...) 通过 SelectResponse 和 mappings.Get<T>(name)/Fallback(value) 选择，名称不存在或来源不匹配会失败。

IFailure.Title/Detail 初始化 ProblemDetails，type 默认为 about:blank，不填 instance，status 与 HTTP 一致。不自动铺开 Error，不重复添加 code/message。ConfigureProblem 按字段补丁；Problem(...) 或 Body<ProblemDetails>(...) 替换正文槽并遮蔽更早/更低优先级补丁，后续或更具体补丁仍生效。工厂返回冲突的 status 会失败。IProblemDetailsService 使用原生宿主定制与输出。Result(writer, explicit HttpBodyDescription) 支持 SSE、文件等原生协议 Writer，并显式声明媒体类型和 Schema。

## 上下文与身份

边界使用原生 Http.User 或配置的 NOF 凭据提供者完成认证。Context 只接收显式选择的语义值：已认证租户 Claim、租户意图 Header 或命名 Item。不会批量复制 Header，不把 HttpContext、服务容器或可变引用对象放入语义 Context；常量在配置时捕获，自定义 Provider 只能返回不可变标量。业务/Filter 的 Context 修改不回流到输出配置。HttpOutputContext 提供 Http、保存的边界 Context、Operation、CancellationToken，以及原生命名路由 LinkTo。

## 运行时 OpenAPI 与 Native AOT

原生端点最终约定根据请求元数据、操作分支及有效调用计划编译冻结的 HttpEndpointBinding。执行和运行时 OpenAPI 转换器读取同一份绑定；ApiExplorer 响应元数据、Body 投影、参数、有限状态、类型化 Header、具名候选和 Problem 扩展均由此描述。启动/生成文档不执行 Handler、Selector、Provider 或 Map，不探测 lambda、switch 或 IL。关闭反射时，宿主 JSON Context 必须注册实际请求/正文/投影/扩展类型。冒烟测试已覆盖关闭 JSON 反射时的泛型操作映射与 OpenAPI，原生发布由 CI 检查。

## 破坏性迁移

将注册式端点模块改为 MapFeatureEndpoints 普通扩展，由宿主显式调用；删除 MapNOFEndpoints、HttpRequestHeaderOptions，显式映射健康检查/OIDC 和 Context 来源。尽量将描述符路由调用改为 MapGet/MapPost 等的 <Service.Operation> 形式；ProjectReference 消费端需显式引用 Hosting 分析器，NuGet 运行时包已内嵌。Failure 的 Code/ErrorCode/Message 改为 Title/Detail，自定义属性改为 FailureCase(name, title, typeof(Error))。按具体失败和真实 HttpStatusCode 分支，不解析展示文本。调用规则改为对应目标最后一次写入生效。
