# JSON 执行上下文与可靠投递

[English](../../en/design/execution-context.md) · [统一操作](unified-operations.md) · [事务发件箱](transactional-outbox.md)

Context 是 JSON 对象的不可变快照。键是大小写不敏感的字符串，值只允许 JSON 字符串、有限数字、布尔值或 null；不接受数组、对象、任意 CLR 实例或对象键。不引入 CLR 类型名目录或动态类型加载。

```csharp
var context = Context.Empty.WithTenantId("tenant-a")
    .WithItem("correlation", "order-42")
    .WithItem("priority", 3)
    .WithItem("audit", true)
    .WithItem("optional", null);

int priority = context["priority"]!.GetValue<int>();
JsonObject snapshot = context.Items;
```

WithItem/WithItems/WithoutItem 返回新 Context。Items、索引器和 TryGetItem 返回分离的节点，修改快照不会改变正在执行或之前的 Context，也不会改变 Context.Empty。WithItems 接受 JsonObject，复制并验证所有值。HTTP 上下文来源及测试作用域采用相同模型。原对象键改为业务自己的字符串常量，枚举、日期和标识符明确表示为 JSON 值；使用 `JsonNode.GetValue<T>`/`TryGetValue` 读取，不能再按任意 CLR 对象强制转换。

## Outbox 保存完整快照

```csharp
await outbox.EnqueueAsync(new OrderCreated(orderId), invocation.Context,
    new Dictionary<string, string?> { ["partition"] = orderId.ToString() }, ct);
await unitOfWork.SaveChangesAsync(ct);

public sealed class ForwardOrder(RabbitMQMessageClient client) : OutboxHandler<OrderCreated>
{
    public override ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext delivery, CancellationToken ct)
        => client.PublishAsync("orders", "created", message, delivery.Headers, ct);
}
```

入队时保存负载、所有语义上下文项、显式 Headers、规范化持久化租户、稳定消息 ID 及入队追踪。完整 JSON 上下文保存在现有 Headers 文档的保留键 X-NOF-Context 中，无需增加数据库列或负载包装。X-Message-Id、X-Tenant-Id、traceparent 和 X-NOF-Context 由框架管理。

每次尝试都在构造 scoped Outbox Handler 前还原并绑定持久化 Context。OutboxDeliveryContext 直接提供 Context、MessageId、Attempt 和可直接发送的只读 Headers。原生适配器转发 Headers，将消息 ID 同时写入传输的原生字段；无需注入或调用 MessageDelivery，该服务及注册已删除。重试保留入队快照和 ID，不借用当时其他请求或后台作用域的上下文。

消息端点在独立 Scope 内恢复 Context，直接执行操作及正常权限/Filter 管道。NOF 不再提供 Inbox 存储、消费去重或消费重试调度。业务负责幂等和事务/保存边界，RabbitMQ 在操作完成后 ACK；原生适配器负责重投和死信。Memory 等待执行并传播失败。只有 AddOutbox 开启持久化发件及 Worker，队列映射不安装持久化。见[直接端点与迁移](transactional-outbox.md)。

旧待发记录或未提供 X-NOF-Context 的原生消息，只能还原原有租户/头数据，无法补回旧版从未保存的项。本次不需要数据库迁移，保留消息 ID、负载、Headers 和收据历史；同步更新包并迁移破坏性的 Context API。

## 链式宿主配置

```csharp
services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "app:{tenantId}:")
    .AddMemoryCacheService();

services.AddRabbitMQ(factory => factory.Uri = connectionUri)
    .ConfigureRabbitMQ(OrdersMessaging.Configure)
    .ConfigureRabbitMQ(AuditMessaging.Configure);
```

ConfigureCacheServiceOptions 替代缩写的 CacheServiceSelector.Configure，可在选择 Provider 前后调用，按原生 Options 注册顺序组合。AddRabbitMQ 和 ConfigureRabbitMQ 已经返回 IServiceCollection，直接链式使用，无需增加 Selector 或 Builder 层。多次配置仍共享一个 RabbitMQ 生命周期，拓扑始终先于消费者启动。
