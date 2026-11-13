# 应用服务与 HTTP 端点模块

[English](../../en/design/application-requests.md) · [配置节点 REST 示例](configuration-rest.md) · [架构决策](0001-application-core-and-adapters.md)

应用服务的一个操作就是一个用例。保留 proto 风格契约、生成的 `Service.Operation` 基类和
override 工作流。契约只包含业务请求、响应和应用策略元数据，不再声明 HTTP 路由或绑定。

```csharp
public partial interface IOrderService : IApplicationService
{
    OrderDto? Find(FindOrderRequest request);
    [FailureCase("Rejected")]
    OrderDto Save(SaveOrderRequest request);
}
public partial class OrderService : ApplicationService<IOrderService>;
public sealed class FindOrder(IOrderQueries orders) : OrderService.Find
{
    public override Task<OrderDto?> HandleAsync(
        FindOrderRequest request, InvocationContext context, CancellationToken ct)
        => orders.FindAsync(request.Id, context.Context, ct);
}
```

每个声明只有一个引用类型请求和同步业务返回值。Context、CancellationToken、Task 属于
生成的服务端 API。支持 DTO、可空值和业务联合类型。IRpcResponse 和通用 Contract Result
已删除。FailureCase 生成操作专属 Result，供 Handler 继承；Empty 表示无载荷成功，
成功消息属于业务 DTO。
不生成客户端接口、HTTP Client 或 Local Client。

## 注册与本地调用

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();
var order = await OrderService.Find.InvokeAsync(services, request, context, ct);
```

应用程序集初始化器将各操作实现注册为 Scoped 服务，宿主只注册一次服务容器，不逐操作注册。
显式添加各类型所在程序集，包括包含端点模块的宿主程序集。操作身份为
“服务契约 + 操作名”，不同操作可以共用请求/响应模型。

本地调用创建独立作用域，直接传递 Context，复制调用方作用域里的身份快照。授权和租户策略
先于 Handler 解析执行，调用方取消直接传播。不序列化 Header，不重复验证 Token。
直接解析 Handler 再调用 HandleAsync 会绕过这些策略。

## 原生 HTTP 端点模块

`IHttpEndpointModule` 按 API 功能组织路由，内部使用原生 MapGet/MapPost、路径/请求体/查询
绑定、TypedResults、端点过滤器和 OpenAPI。模块标注 `[AutoInject(ServiceLifetime.Singleton)]`，
添加其程序集为 ApplicationPart 后自动注册，Build 后显式调用 app.MapNOFEndpoints() 映射。
宿主不用逐操作注册，也不用集中维护所有路径、绑定和状态码映射。

```csharp
[AutoInject(ServiceLifetime.Singleton)]
public sealed class OrderEndpoints : IHttpEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/orders/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var order = await http.InvokeAsync(OrderService.Find.Descriptor, new FindOrderRequest(id), ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        }).AddEndpointFilter<ServiceRejectionHttpFilter>();
    }
}
```

生成的 `Descriptor` 只携带契约、操作名和可空性，没有 HTTP 依赖。
`HttpContext.InvokeAsync` 使用它执行 HTTP 身份认证和应用入站策略，保留策略响应头，返回
声明的业务值。端点不用填写操作名字字符串，也不用重复填写请求/响应泛型参数。

`ServiceRejectionHttpFilter` 将策略拒绝转换为原生 ProblemDetails，保留授权状态码和 challenge。
Handler 返回的业务 Failure 由功能模块自行映射。宿主按需要配置 ASP.NET Core 的
AddProblemDetails、状态码页和异常处理。端点授权、文件、内容协商等 Web 能力继续使用原生框架。

同一编译中生成服务容器时，Native AOT 端点应使用显式返回类型的方法。ASP.NET Core 的
请求委托生成器无法从另一个生成器尚不可见的产物推断 lambda 返回类型。

示例 ConfigNodeEndpoints 已落地节点 CRUD、文件管理、活跃文件、配置查询和 SSE。
OAuth 演示路由在另一个模块。Contract 项目不引用 Hosting，也不包含 HTTP 注解。
[完整 REST 示例](configuration-rest.md)包含实际请求、Handler、路由、宿主注册和验证方式。

## 执行与协议边界

ServiceExecution 区分完成值（包括声明允许的 null）和策略拒绝，只用于调用过程，不是线上
响应包装，也不是 Handler 返回契约。所有生成的类型化 InvokeAsync 都通过
ServiceRejectedException 报告策略拒绝，返回 Result 的操作也一样，不再生成结果专用失败工厂。
业务 Failure 分支仍作为普通业务值返回。

旧 HTTP RPC 和 JSON-RPC 适配器已彻底删除，包括 HttpEndpoint、HttpVerb、传输注解、
自动路由映射、传输注册、专用响应写入器及协议读取器，不保留兼容层。契约始终保持传输无关，
Contract 分析器只验证服务签名。HTTP 路由在端点模块显式声明，响应使用原生 TypedResults
和 ProblemDetails。SSE 使用 TypedResults.ServerSentEvents，调用方使用平台 SseParser。
Result 不再定义 CreateEmptyResponse。

请求/响应出站 executor、delegate、context、middleware API 和 Client 生成继续保持移除。
远程调用方自行维护 HttpClient/DelegatingHandler；消息出站元数据由投递组件直接准备，详见[统一调用扩展](invocation-extensions.md)。

## 破坏性迁移

| 旧 API | 当前 API |
| --- | --- |
| IRpcService / RpcServer / AddRpcServer | IApplicationService / ApplicationService / AddApplicationService |
| 生成客户端及 Client 标记 | 删除；显式 HTTP 集成或服务端 InvokeAsync |
| Contract 中的 HttpEndpoint / 传输注解 | 删除；HTTP 端点模块显式声明路由 |
| IRpcResponse / CreateEmptyResponse | 删除；业务联合类型及适配器显式转换 |
| 策略拒绝时自动构造 Result Failure | ServiceRejectedException；HTTP 过滤器映射策略拒绝 |
| 原生端点里的操作名字符串 | Service.Operation.Descriptor |
| AddRequestHandler / 独立 executor 层级 | 生成的操作 override 及程序集注册 |

契约、实现、适配器和宿主需一同升级。策略将旧 SetResponse(Failure) 改为 Reject，业务
Result 的 Failure 仍是业务数据。重新构建生成产物，删除自定义构建工具残留的客户端文件。
既有标签和发行版保持不可变；回滚时恢复配套的旧包与调用方代码。

[声明可预期失败](expected-failures.md)可为每个操作生成封闭结果；方法声明表示成功类型，Descriptor 表示实际执行返回类型。

[统一调用扩展](invocation-extensions.md)替代三套入站中间件，由宿主选择执行器；消息投递固定准备元数据，不再保留出站扩展链。

[当前统一操作与消息绑定 API](unified-operations.md)。
