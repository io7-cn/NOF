# 事务性 Outbox 与直接消息端点

[English](../../en/design/transactional-outbox.md) · [RabbitMQ 示例](rabbitmq-sample.md) · [单向操作](one-way-operations.md)

Application 通过 IOutbox 暂存传输无关的意图，基础设施 OutboxHandler<T> 选择原生客户端和目的地。消费端直接执行 OperationHandler；NOF 不再提供 Inbox、收件表、消费去重或消费重试调度，业务自行负责幂等性。

## 业务暂存

```csharp
var id = await outbox.EnqueueAsync(message, invocation.Context,
    headers: new Dictionary<string, string?> { ["correlation-id"] = correlationId },
    cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

EnqueueAsync 返回 ValueTask<Guid>，表示暂存快照的标识，不代表副作用已完成。它立即序列化载荷、Context 和可选 IReadOnlyDictionary<string,string?> Headers，不发送、不保存。业务变更与意图使用同一个作用域工作单元；显式事务还必须 CommitAsync。释放或回滚丢弃未提交意图。EF/NHibernate Store 共享业务的原生会话和事务。开发用内存后端仅保存 Outbox 状态，不提供跨进程持久化或业务实体存储。

持久化租户具有权威性，显式冲突会被拒绝。保存所有 JSON 标量 Context 项、自定义 Header、规范化租户、稳定消息 ID 和原始 Trace，不保存 Claims、IUserContext 或运行时对象。消息 ID、租户、Trace、X-NOF-Context 是保留键。每次重试在构造 Scoped Handler 前恢复 Context，并提供只读、可直接发送的 OutboxDeliveryContext.Headers。适配器直接转发这些 Header；MessageDelivery 和 OutboxEnqueueOptions 已删除。

## 基础设施注册与处理器

```csharp
builder.AddApplicationPart(typeof(DeliverOrder).Assembly);
builder.Services.AddOutbox();
builder.Services.Configure<OutboxOptions>(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.RetryDelay = TimeSpan.FromSeconds(5);
});

public sealed class DeliverOrder(OrderClient client) : OutboxHandler<OrderCreated>
{
    public override ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext delivery, CancellationToken ct)
        => client.PublishAsync(message, delivery.Headers, ct);
}
```

AddApplicationPart 自动注册可访问、非泛型的具体处理器与强类型投递委托，不构造 Handler 或启用 Worker。每个 Handler 只实现一个载荷类型的 HandleAsync，无需 Attribute、partial、MessageType 或 SchemaVersion。持久化标识推导为 typeof(T).ToString()，含命名空间和闭合泛型参数、不含程序集版本；宿主内必须唯一且不超过现有 512 字符列。NOF330 检查访问性和旧元数据，NOF331 检查重复载荷处理器。开放泛型适配器可通过 AddOutboxHandler<TMessage,THandler>() 显式闭合注册。持久化键只通过已注册委托解析，不使用 Type.GetType 或动态加载。

OutboxDeliveryContext 提供 MessageId、Context、Attempt 和 Headers。Handler 正常返回表示具体客户端的交付约定已完成，不保证远端业务副作用只执行一次。外部副作用使用稳定业务键或服务商幂等键。正常返回后允许标记 Sent，技术异常进入 Outbox 重试，未知 Schema 或显式永久失败进入 Failed 而不阻塞其他消息。认领、完成、失败和释放均校验所有权；保留过期且重试耗尽的认领恢复。

## 按能力装配与独立 Worker 宿主

AddNOF、AddMemoryMessaging 和 RabbitMQ MapQueue 都不安装 Outbox Store、模型或 Worker。AddOutbox 显式安装暂存、未选原生/自定义 Store 时的消息专用内存持久化，以及发件处理/清理两个 Worker。重复调用幂等、保留自定义 Store，在 AddNOF 前后都有效，重复 Schema/Handler 仍报错。直接配置扁平 OutboxOptions，移除 TransactionalMessageOptions 及其 Inbox/Outbox 嵌套配置。

OutboxOptions.RunWorkers=false 保留暂存和存储，但停止本宿主发件处理与清理。独立 Worker 宿主使用相同 Schema 和持久化。调度参数只在运行 Worker 时验证。停止 Worker 不删模型、待处理消息或认领状态。

## 消费执行与业务幂等

RabbitMQ 为每次交付创建独立 Scope，恢复 Context，执行正常权限和 Filter 管道；操作与业务负责的提交边界完成后才 ACK。异常和 Filter 拒绝不能视为成功交付，NACK、重投和死信由原生适配器/宿主配置决定。原生 MessageId 优先于旧 Header，允许非 Guid 标识，不会为每次重投生成新 ID。未携带标识就保持缺失，业务可以改用业务键。不恢复生产者 Claims。

适配器不强制创建数据库事务，也不自动保存业务数据。Handler 或业务 Filter 负责 Save/Commit。提交后 ACK 丢失仍会再次执行；使用数据库唯一业务键/版本，在同一事务内保护副作用，单独查询是否存在不能解决并发。消费时新暂存的 Outbox 应与业务修改同事务提交；外部副作用使用 Outbox 或服务商幂等能力。

Memory SendAsync 等待本地 Handler 完成并传播异常/取消，PublishAsync 为每个订阅创建独立 Scope。上游 Outbox 可能重投部分成功的广播，因此已成功订阅也需要业务幂等。直接 Memory 调用不承诺进程重启恢复。Contract 的 void 仍无业务回信，等待传输执行不会新增 Result 或 FailureCase 响应。

NOF 不保证发件或收件顺序，基础设施负责原生并发、调度、重试和顺序要求。看似顺序控制的自定义 Header 仍是不透明值；CreatedAtUtc/Id 排序仅用于 Outbox 批次调度。RabbitMQ 拓扑、预取、重试和死信策略属于基础设施，应避免无限立即 Requeue。

## 破坏性迁移与已有数据库

仅在生产者或 Outbox Worker 中将 AddTransactionalMessages 改成 AddOutbox。删除 Inbox 配置、自定义 IInboxStore、收件注册表和 Worker 集成。直接配置 OutboxOptions，例如 options.PollingInterval，不再是 options.Outbox.PollingInterval。消费方只有需要暂存新意图时才注册 Outbox；启用新消费者前，将原先依赖 Inbox 的去重迁移为业务约束。

停止旧消费者/Worker，备份所有租户数据库。旧 Inbox 消息可能早已被 Broker ACK，必须用旧应用处理完，或显式导出并按审查后的载荷、元数据重放。按需导出已完成的去重历史，切换期间不要同时运行新旧消费者。示例 20261116000000_RemoveInbox 迁移先锁定旧表，存在 Pending/Failed 就拒绝，然后仅删除 NOFInboxMessage，不改 Outbox，不猜测重放目的地。降级必须恢复匹配备份和旧程序；本次修改不执行线上迁移。

历史 GenericOutbox、RemoveMessageOrdering、RemoveInboxClassification、SimplifyOutboxMetadata 迁移保持不变，旧库应按顺序审查。SimplifyOutboxMetadata 只映射示例两种未改变的 v1 载荷，拒绝不支持的待处理版本并删除 SchemaVersion。载荷命名空间/类型变更仍需显式键与格式迁移，不提供未知消息自动转换或兼容别名。

完整上下文传播和业务投影幂等示例见 [JSON 上下文](execution-context.md) 与 [RabbitMQ 示例](rabbitmq-sample.md)。
