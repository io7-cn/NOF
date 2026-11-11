# 传输无关的应用请求

[English](../../en/design/application-requests.md) · [架构决策](0001-application-core-and-adapters.md)

ADR 0001 第一阶段引入作用域级执行器。契约位于 NOF.Application，执行与注册位于
NOF.Infrastructure。普通 ServiceCollection 即可运行，无需 Web、序列化器、数据库或消息代理。

实现 `IRequestHandler<TRequest,TResponse>` 或继承 `RequestHandler<TRequest,TResponse>`。
`HandleAsync(request, context, cancellationToken)` 返回 Task<TResponse>，无 IRpcResponse
约束；可以返回 DTO、业务 Union 或 null。

```csharp
builder.AddApplicationPart(typeof(GetOrder).Assembly);
var execution = await executor.ExecuteAsync<GetOrder, GetOrderRequest, OrderDto?>(request, context, ct);
switch (execution)
{
    case RequestCompleted<OrderDto?> completed:
        // 使用 Response，包括 null 或业务 Union。
        break;
    case RequestRejected rejected:
        // 由适配器映射 Kind 和 Failure。
        break;
}
```

应用层源码生成器发现具体的封闭 Handler 类，包括直接实现接口、间接继承和 record 类，
通过已有的程序集初始化器生成 Scoped 注册。NOF 宿主已自动加入调用程序集；独立的
Application 程序集只需一次 AddApplicationPart，不需要逐个注册 UseCase。
整个过程不使用运行时类型扫描或动态泛型构造。注册以具体 UseCase 类型为身份，
不以输入/输出组合为唯一键。多个 UseCase 可以共用这些模型；一个类实现多个请求
契约时共享一个 Scoped 实例。TryAddScoped 保留已有的显式 DI 替换。

独立 ServiceCollection 可以调用一次 AddNOFRequests，再运行生成的程序集初始化器；
测试或特殊装配也可以用 AddRequestHandler 显式选择 Handler；该注册按 UseCase 类型
幂等，可以与自动注册共存。
调用方为每个工作单元创建 DI 作用域，请求和 Context 不能为 null。
RequestCompleted 是进程内执行分支，不是线上成功包装；Result 中的业务 Failure
仍然属于执行完成。异常和取消直接传播，未初始化的执行 Union 会抛出异常。

通过 DI 注册 `IRequestMiddleware<TUseCase,TRequest,TResponse>`，先注册的在最外层。
策略同样按 UseCase 类型寻址，共用 DTO 不会隐式共用授权等策略。通用策略可以一次
注册开放泛型中间件，特定用例的策略则注册对应封闭类型。
InvokeAsync 可以拒绝，或用规范化后的请求/上下文调用 next。到达终端委托才构造
Handler，此时 Context.Current 已绑定；await 和异常后恢复原值。策略实例会提前解析，
因此应将租户相关依赖和工作推迟到规范化之后。

`AddNOFRequests()` 仅注册执行器，不自动安装认证、授权、验证、事务、重试、幂等性，
也不解释 RPC 特性。新入口必须建立可信上下文并配置所需策略。共享策略迁移属于
ADR 第二阶段，已有 RPC 检查继续生效。

原生 ASP.NET Core 端点负责绑定、授权、状态码、响应头、TypedResults、文件和 OpenAPI。
完成时返回业务值，在边界映射拒绝，不要序列化 RequestExecution。认证拒绝需要使用
宿主认证方案的 challenge 行为。原生端点不会因为调用执行器而继承 RPC RequirePermission。

已有生成式 RPC Handler 可以桥接到同一个返回 DTO 的用例：

```csharp
public sealed class ReadOrder(IRequestExecutor executor) : OrderServer.Read
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => ExecuteUseCaseAsync<ReadOrderUseCase, OrderDto>(executor, request, context,
            static dto => Result.Success(dto), cancellationToken);
}
```

桥接投影业务完成值，并用 TResponse.CreateFailure 映射策略拒绝；业务响应与 RPC 响应
相同时有简化重载。原有 RPC 安全管线和线上契约继续生效。

示例 GetRootConfigNodesUseCase 直接返回 DTO。AotSmoke 用同一个 ReadOrderUseCase
验证生成式 RPC 和原生 Minimal API，使用源生成 JSON，并由 CI 验证 Native AOT。
