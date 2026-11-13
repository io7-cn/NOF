# 应用服务与 HTTP 端点模块

[English](../../en/design/application-requests.md) · [配置节点 REST 示例](configuration-rest.md) · [架构决策](0001-application-core-and-adapters.md)

应用服务的一个操作就是一个用例。保留 proto 风格契约、生成的 `Service.Operation` 基类和
override 工作流。契约只包含业务请求、响应和应用策略元数据，不再声明 HTTP 路由或绑定。

```csharp
public interface IOrderService : IApplicationService
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

使用返回原生 RouteGroupBuilder 的普通 MapFeatureEndpoints 扩展，以 `group.MapGet<OrderService.Find>("/{id}")` 映射操作，独立配置 Request 成员/完整构造和 Response<TCase>。Application 操作通过静态 Descriptor/Describe 实现 IServiceOperationDefinition，保持类型安全及 AOT 元数据；轻量 Hosting 生成器提供具体请求 lambda 类型，不分析函数、不依赖生成输出。宿主显式调用功能映射，不注册端点模块 DI，不集中列举 Handler。

默认 DTO/Empty/null/失败分别为 200 JSON/204/200 JSON null/422 ProblemDetails，404/409 等按具体失败类型配置。Context 只传入显式语义值，不从业务回流。运行时 OpenAPI 读取最终不可变绑定，不执行用户函数。中间件、异常处理及协议仍由 ASP.NET Core 负责。参见 [HTTP 适配](http-adaptation.md) 和[完整 CRUD](configuration-rest.md)。

## 执行与协议边界

业务 Handler 保持 Task<TBusiness>，Filter/next 返回 ValueTask<object?>，完整 InvokeAsync 返回 Task<object?>。调用方匹配返回的业务 union/值或生成的 Filter 分支。InvocationContext 不再保存可变结果，预期失败不再通过异常传播。有效计划校验所有允许的业务与 Filter 分支，详见[调用扩展](invocation-extensions.md)。

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
| 策略拒绝时自动构造 Result Failure | 返回 Filter 失败值；显式原生 HTTP 映射 |
| 原生端点里的操作名字符串 | Service.Operation.Descriptor |
| AddRequestHandler / 独立 executor 层级 | 生成的操作 override 及程序集注册 |

契约、实现、适配器和宿主需一同升级。策略直接返回声明的 Filter 失败值，替代 SetResponse/Reject；业务
Result 的 Failure 仍是业务数据。重新构建生成产物，删除自定义构建工具残留的客户端文件。
既有标签和发行版保持不可变；回滚时恢复配套的旧包与调用方代码。

[声明可预期失败](expected-failures.md)可为每个操作生成封闭结果；方法声明表示成功类型，Descriptor 表示实际执行返回类型。

[统一调用扩展](invocation-extensions.md)替代三套入站中间件，由宿主选择执行器；消息投递固定准备元数据，不再保留出站扩展链。

[当前统一操作与消息绑定 API](unified-operations.md)。
