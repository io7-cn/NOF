# 单向操作与直接消息端点

[English](../../en/design/one-way-operations.md) · [统一操作](unified-operations.md) · [事务发件箱](transactional-outbox.md)

Contract 返回类型表达是否具有业务回信能力，而不只是有无有效载荷。

| Contract | Handler | 面向生产者的业务回信 |
|---|---|---|
| void Send(Message) | Task HandleAsync(...) | 没有，明确单向 |
| Empty Update(Message) | Task<Empty>，声明 FailureCase 时为 Task<Result> | 无载荷成功或已声明失败 |
| Dto Read(Query) | Task<Dto>，声明 FailureCase 时为 Task<Result> | 成功数据或已声明失败 |

```csharp
public interface IMailService : IApplicationService
{
    void Send(SendMail request);
    [FailureCase("Denied")]
    Empty ChangeSettings(ChangeMailSettings request);
}

public partial class MailService : ApplicationService<IMailService>;
public sealed class SendMailHandler(IMailGateway gateway) : MailService.Send
{
    public override Task HandleAsync(SendMail request, InvocationContext invocation, CancellationToken ct)
        => gateway.SendAsync(request, ct);
}
```

void 生成 OperationHandler<TRequest> 与 OperationDescriptor<TRequest>，响应元数据是 typeof(void)，没有业务结果分支、Result、失败分支或返回业务结果的 InvokeAsync。执行宿主仍等待 Handler Task 完成，以维持事务、重试和取消；不会生成 async void、忽略 Task 或自动 Task.Run。

void 标记 FailureCase 报 NOF220；旧 NOF209 禁止 void 的诊断和自动改成 Empty 的修复删除。请求/响应描述符不能伪装成 void，void 也不能伪装成 Empty。明确等待回信的 IOperationExecutor.ExecuteAsync 拒绝 void；宿主使用 OperationExecutor.ExecuteInScopeAsync 等待内部处理，不将处理记账返回给生产者。

## 端点直接执行

MapQueue 只接受 void 且没有业务 FailureCase 的操作。适配器创建独立 Scope，在构造依赖之前恢复语义 Context，通过 OperationExecutor.ExecuteInScopeAsync 以 InvocationOrigin.Message 执行。权限及 Global/Service/UseCase Filter 继续生效，拒绝属于交付失败，不能 ACK。框架不保存收件、去重、轮询，也不自动 SaveChanges 或创建事务。业务 Handler 或 Filter 负责提交边界，RabbitMQ 等待执行完成后 ACK，失败使用原生重试/死信配置。业务提交后 ACK 丢失仍会再次执行，因此业务必须自行幂等。

Memory SendAsync 等待本地执行并传播异常/取消，PublishAsync 独立执行每个本地订阅。直接 Memory 调用和 NOF 都不负责跨进程保存未完成消费。等待执行不改变 void 的无回信语义，不向发送者返回 Result 或业务失败。

## HTTP 与迁移

Empty 仍是有回信的操作，可使用 FailureCase 联合结果；void 没有返回结果的调用 API。需要回信的 HTTP/本地调用拒绝 void。异步 HTTP 接收必须在真实可靠 Broker 或业务自行提供的持久化机制接收后再返回 202；Task.Run 和直接 Memory 分发不代表可靠接收。

删除 Inbox 注册、自定义 Store、收件元数据和调度配置。生产者显式选择 AddOutbox 和扁平 OutboxOptions；纯消费者只注册传输端点与业务依赖。切换前处理已被 ACK 但仍未完成的旧 Inbox 消息，RemoveInbox 防护迁移会拒绝未完成记录。见 [Outbox 与迁移](transactional-outbox.md)。

## Application 内部反应

具体操作无需 Contract 方法或 partial：

```csharp
public sealed class CountOrder(IOrderMetrics metrics) : OperationHandler<OrderCreated>
{
    public override Task HandleAsync(OrderCreated request, InvocationContext context, CancellationToken ct)
        => metrics.RecordOnceAsync(request.OrderId, ct);
}

builder.AddApplicationPart(typeof(CountOrder).Assembly);
builder.Services.AddRabbitMQ(...)
    .ConfigureRabbitMQ(rabbit => rabbit.MapQueue<CountOrder>("orders.metrics"));
```

AddApplicationPart 发现强类型描述和 Scoped 实现。示例 CountOrder 通过投影端口使用原子唯一业务键。独立操作的 ContractType 为 null，只有 Contract 接口/方法声明权限，Global 与 UseCase Filter 仍然生效。IOperationExecutor.ExecuteAsync 是本地有回信边界，宿主适配器使用同一个具体 OperationExecutor.ExecuteInScopeAsync，不再增加另一套执行器或 Handler 接口。
