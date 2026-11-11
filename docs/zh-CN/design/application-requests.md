# 应用服务与生成的操作

[English](../../en/design/application-requests.md) · [架构决策](0001-application-core-and-adapters.md)

应用服务的一个操作就是一个用例。保留 proto 风格的服务契约、生成的 `Service.Operation`
基类和 override 工作流。完全移除 Client 生成：不生成客户端接口、HTTP Client 或 Local Client。

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

契约只声明一个请求和同步业务返回类型。Context、CancellationToken、Task 属于生成的
服务端 API。支持 DTO、可空值、业务联合类型和可选的 Result。`Success<T>` 仍然只是携带
成功消息的可选包装，不要求实现 IRpcResponse。

## 注册与调用

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

var order = await OrderService.Find.InvokeAsync(services, request, context, ct);
```

宿主只需注册一次服务容器，应用程序集初始化器自动把各操作实现注册为 Scoped 服务，
不需要逐操作注册。NOF 宿主已添加调用程序集，独立的 Application 程序集再添加一次即可。
生成的静态 InvokeAsync 是服务端调用胶水，通过 IApplicationServiceInvoker 建立新调用
作用域，直接传递 Context，并复制调用方作用域里的身份快照，再执行入站应用策略。
本地调用不编码 HTTP Header、不重复验证 Bearer Token，应用授权仍会执行。直接解析 Handler 后调用 HandleAsync 会绕过策略。
操作身份是“服务契约 + 操作名”，不同操作可以共用请求/响应类型而拥有各自的实现和策略。

## HTTP 适配与调用方

未标注传输属性的契约不自动暴露 HTTP 路由；TransportOverMemory 仍可显式声明仅本地。
可选的 TransportOverHttp/HttpEndpoint 保留现有服务端适配能力，包括 Controller/JSON-RPC
路由、绑定、序列化元数据、SSE 和 OpenAPI，不生成任何 Client。

原生 ASP.NET Core 端点可调用 Service.Operation.InvokeAsync，再返回 TypedResults、文件等
宿主结果。Handler 无需依赖 IResult 或 HttpContext。端点授权由宿主负责，调用时仍执行
NOF 应用授权及租户策略。独立的 REST 端点模块 API 属于后续工作，当前没有引入新的端点 DSL。

远程调用方自行维护 HTTP 集成，或根据 HTTP API 使用外部工具；引用 Contract 不再获得
网络代理。示例 UI 使用应用自己维护的 ISampleApi/IOAuthDemoApi：Server 适配器调用
InvokeAsync，WASM/MAUI 适配器显式使用 HttpClient。它们是示例代码，不是框架生成产物；
HTTP 方法、路径、绑定和编解码选择需与对应服务端端点一起维护。

## 执行语义

显式可空响应允许 null，非可空操作返回 null 则是执行错误。Handler 返回的 Failure 或业务
联合类型仍是业务值。策略通过 RequestInboundContext.Reject(failure) 拒绝执行，与
SetResponse(value) 分离。策略和上下文规范化先于 Handler 解析；调用方取消直接传播。

ServiceExecution 区分完成值（包括 null）与策略拒绝，只用于调用过程，不是线上的响应包装，
也不是 Handler 返回契约。静态 InvokeAsync 对普通响应以 ServiceRejectedException 报告拒绝；
显式 IRpcResponse 结果保留静态生成的失败工厂，因此 Result 不需要反射转换。需要自行映射
拒绝的适配器可以注入 IApplicationServiceInvoker，匹配 ServiceCompleted/ServiceRejected。

同时删除请求/响应出站抽象：IRequestOutboundPipelineExecutor、RequestOutboundHandlerDelegate、
IRequestOutboundMiddleware、RequestOutboundContext 和 AddRequestOutboundMiddleware。
远程令牌和 Header 行为交给原生 HttpClient DelegatingHandler，HTTP 跟踪使用 .NET 内置能力。
示例下游 Handler 从单次 HTTP 请求的 Options 读取数据，不依赖服务契约。消息投递的
Command/Notification 出站管线继续保留。HTTP 入站 Header 过滤配置改名为
HttpRequestHeaderOptions（原 HttpHeaderOutboundOptions）。

## 破坏性迁移

| 旧 API | 当前 API |
| --- | --- |
| IRpcService | IApplicationService |
| `RpcServer<TService>` | `ApplicationService<TService>` |
| `RpcHandler<TRequest,TResponse>` | 生成的 Service.Operation / `ServiceOperation<TRequest,TResponse>` |
| `AddRpcServer<TService>`() | `AddApplicationService<TService>`() |
| `IRpcClient<TService>` / `IApplicationServiceClient<TService>` | 删除，不提供替代的 Client 标记 |
| 生成的 I…ServiceClient / Http…ServiceClient | 应用自己维护 HTTP 集成 |
| 生成的 Local…ServiceClient | Service.Operation.InvokeAsync(services, request, context, ct) |
| AddLocalApplicationServiceClient / GetRpcClient | 删除；测试 `CallAsync<TResult>` 接受调用委托 |
| 强制传输声明 / IRpcResponse | 可选 HTTP 元数据；普通、可空或联合类型业务响应 |

移除原型 IRequestHandler/IRequestExecutor/AddRequestHandler 和转发类，直接实现生成的操作
override。将 SetResponse 的旧 Failure 重载改为 Reject。契约、实现、调用方和宿主包需一同
升级；自定义构建工具残留的生成客户端文件也需删除。AOT 冒烟覆盖 DTO、可空返回、业务联合
类型、共用模型、策略拒绝、服务端 HTTP 绑定、原生 HTTP 和本地调用，全程不使用生成客户端。
