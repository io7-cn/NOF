# 由宿主选择的统一调用扩展

[English](../../en/design/invocation-extensions.md) · [开发指南](../README.md)

NOF.Application 为服务操作、命令和通知定义一个扩展点：
`IInvocationFilter.InvokeAsync(InvocationContext, object, InvocationDelegate, CancellationToken)`。
Application 中的 InvocationContext 组合语义 Context，提供操作描述符、入口来源、输入与 Handler 元数据以及执行结果，不依赖 HTTP。
预期业务结果仍使用操作专属 Union，策略拒绝与业务返回分开。

注册扩展只声明扩展，不会选择执行器，也不保证扩展自动生效。宿主通过
`IInvocationPipeline` 决定是否执行、在哪执行以及执行顺序。
NOF.Infrastructure 提供可选的内存默认实现，NOF 宿主构建器默认选择它。

```csharp
// 应用组合：声明可复用扩展，不选择执行器。
services.AddInvocationFilter<AuditFilter>(order: 0);

// 宿主组合：选择默认实现，或者提供自己的 IInvocationPipeline。
services.UseInMemoryInvocationPipeline();
// services.AddScoped<IInvocationPipeline, MyHostInvocationPipeline>();
```

`AddInvocationFilter<T>` 注册 scoped Filter，同一类型只保留一份不可变定义。
默认实现按数值从小到大执行，同序号保持注册顺序；每一阶段到达时才在当前执行上下文中
解析对应 Filter，让归一化和授权先于后续依赖及业务 Handler 的构造。
直接通过 DI 注册 `IInvocationFilter` 也支持，顺序为零，遵循普通 DI 的构造时机；
需要控制构造时机时使用定义注册 API。Filter 调用 `next` 继续，可以通过克隆替换上下文项，
也可以短路；克隆共享执行结果。默认执行器在各阶段及终点绑定并恢复 `Context.Current`，
检查取消，并原样传播异常。

```csharp
public sealed class AuditFilter(ILogger<AuditFilter> logger) : IInvocationFilter
{
    public async ValueTask InvokeAsync(InvocationContext context, object input,
        InvocationDelegate next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Invoking {Operation}", context.OperationName);
        await next(context, input, cancellationToken);
    }
}
```

内置默认顺序为追踪（-400）、指标（-300）、可选的资源服务器认证（-200）、
租户归一化（-100）、授权（-50）、宿主租户切换（-25）。这些是默认宿主配置，
不是所有宿主都必须采用的框架排序协议。替换执行器时，宿主需明确保留所需的认证、授权、
租户、取消和作用域保证；直接调用 Handler 会绕过边界。
服务和消息适配器只提供执行终点，不再分别组装中间件链。

HTTP 路由、绑定、原生中间件、端点过滤器、ProblemDetails 和异常呈现留给 ASP.NET Core。
端点通过 `HttpContext.InvokeAsync` 进入应用边界，不把 HTTP 中间件改装成第二套应用管线。
技术异常由宿主异常处理接收；追踪只记录并重新抛出。取消不会转成业务拒绝。

消息发送不再提供可扩展的出站中间件链。`MessageDelivery` 为直接发送和 Outbox 暂存统一准备
消息 ID、归一化租户及 Producer Activity/追踪头，不序列化任意上下文项或用户身份。
暂存后仍须保存同一个数据库上下文；顺序投递、重试和幂等继续由投递机制负责。
传输专属发送策略由所选 Rider 或宿主集成承担。

## 破坏性迁移

删除原有五套请求/命令/通知入站及命令/通知出站中间件接口、Delegate、出站上下文和
`Add*Middleware` 注册方法。统一实现一次 `IInvocationFilter`，通过 `AddInvocationFilter`
声明，再由宿主选择执行器。原 `Compare` 拓扑约束改为默认执行器中的宿主排序。
出站扩展面删除后，对应 Dispatcher 注入分析器一起移除。
请求和消息上下文统一继承 `InvocationContext` 的 `HandlerMethodInfo` 和 `InputType`。

独立测试或服务容器需显式选择 `UseInMemoryInvocationPipeline()`；NOF 宿主构建器已默认选择。
自定义宿主可以替换 `IInvocationPipeline`，无需修改应用 Filter。
应用、基础设施和宿主包须协调升级。

[当前统一操作与消息绑定 API](unified-operations.md)。

[当前 Contract/Application/Abstraction 边界](contract-boundaries.md)。
