# 应用服务与生成式操作

[English](../../en/design/application-requests.md) · [架构决策](0001-application-core-and-adapters.md)

应用服务操作本身就是 UseCase。保留服务契约、生成的 `Service.Operation` 基类与 override
工作流，不再维护第二套公开 Handler/Executor 或每个操作的转发类。

```csharp
public interface IOrderService : IApplicationService
{
    OrderDto? Find(FindOrderRequest request);
    Result<OrderDto> Save(SaveOrderRequest request);
}

public partial class OrderService : ApplicationService<IOrderService>;

public sealed class FindOrder(IOrderQueries orders) : OrderService.Find
{
    public override Task<OrderDto?> HandleAsync(
        FindOrderRequest request, Context context, CancellationToken ct)
        => orders.FindAsync(request.Id, context, ct);
}
```

契约声明一个请求参数与同步业务返回类型；生成的 Handler 和客户端使用异步方法，接收
Context 和 CancellationToken。返回值可以是 DTO、可空值、普通业务 Union 或可选的 Result。
Result 的成功值仍直接为 T，Success<T> 仅用于显式携带成功消息。服务操作不要求 IRpcResponse。

## 注册与调用

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();
builder.Services.AddScoped<IOrderServiceClient, LocalOrderServiceClient>();
```

宿主沿用原有方式注册服务容器、选择客户端适配器；应用程序集初始化器自动将操作实现注册为
Scoped 服务，不需要逐个注册操作。NOF 宿主已加入调用程序集，独立 Application 程序集只需
加入一次。整个过程不扫描运行时 Handler 类型，也不动态构造泛型。

调用 `IOrderServiceClient.FindAsync(request, context, ct)`。生成的本地客户端复用入站/出站
管线，为调用建立作用域。操作身份是服务契约与操作名：Read、Preview 可以共用输入与输出模型，
仍然具有不同的实现和策略元数据。

## 适配器与执行语义

没有传输特性的契约默认只在进程内调用，不公开 HTTP 路由。TransportOverMemory 仍可显式
声明本地调用。通过 TransportOverHttp 选择 controller HTTP 或 JSON-RPC；仅 HTTP 适配器
解释 HttpEndpoint、绑定、状态码、响应头和线上序列化。

原生 ASP.NET Core 端点可以注入生成的本地客户端，直接返回 TypedResults、文件或其他宿主
响应，业务 Handler 不依赖 IResult 和 HttpContext。端点授权由宿主负责，本地服务调用仍然
执行 NOF 应用授权与租户策略。

只有显式声明可空返回的操作才能返回 null；非空操作返回 null 属于执行错误。业务 Failure
或业务 Union 是完成值。管线策略用 RequestInboundContext.Reject(failure) 拒绝执行，
与 SetResponse(value) 分开。策略与上下文规范化后才解析 Handler，绑定正确的 ambient Context。
调用方请求的取消直接传播，不转成失败响应。

运行时 ServiceExecution Union 区分业务完成值（包括 null）和策略拒绝，不是线上包装或
Handler 返回契约。普通响应的生成客户端通过 ServiceRejectedException 报告拒绝；显式结果
契约保留静态生成的失败工厂，因此既有 Result 失败行为无需反射即可延续。继续复用原有的
授权、租户、事务/outbox 与流式管线。

## 破坏性迁移

| 原 API | 新 API |
| --- | --- |
| IRpcService | IApplicationService |
| RpcServer<TService> | ApplicationService<TService> |
| RpcHandler<TRequest,TResponse> | 生成的 Service.Operation，底层为 ServiceOperation<TRequest,TResponse> |
| AddRpcServer<TService>() | AddApplicationService<TService>() |
| IRpcClient<TService> | IApplicationServiceClient<TService> |
| Local{ServiceName}Client | 保留生成名称与操作调用方式 |
| 必须声明传输方式 | 默认本地调用，HTTP 显式启用 |
| 必须返回 IRpcResponse | 声明 DTO、可空值或业务 Union |

删除原型 IRequestHandler、IRequestExecutor、AddRequestHandler 与 ExecuteUseCaseAsync
转发实现，将业务代码放回生成式服务操作 override。策略使用明确的 Reject 替代原来
SetResponse 的 Failure 重载。契约、实现、生成客户端和宿主包应一同升级。
示例的根配置节点查询已经直接返回 DTO。AOT 冒烟验证 DTO、可空值、无 IRpcResponse 的
业务 Union、共用模型的不同操作、策略拒绝、生成 HTTP、原生 HTTP 和本地服务。
