# 通用事务性发件箱

[English](../../en/design/transactional-outbox.md) · [统一操作](unified-operations.md) · [持久化边界](persistence-boundaries.md)

Application 通过 IOutbox 暂存可靠执行意图。即时调用直接使用具体 HTTP 客户端、SDK、MemoryMessageClient 或原生 RabbitMQ IChannel。基础设施定义每种意图如何处理，以及什么条件代表投递成功。移除 IMessageDispatcher、出站操作路由、ICommandRider/INotificationRider 接口，以及 Outbox 引擎中的 Command/Notification 分支。

## Application API

```csharp
public sealed record SendOrderConfirmation(Guid OrderId, string Email);

await orders.AddAsync(order, ct);
var messageId = await outbox.EnqueueAsync(
    new SendOrderConfirmation(order.Id, customer.Email),
    invocation.Context,
    cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

IOutbox.EnqueueAsync 返回 ValueTask<Guid>。该 ID 标识已暂存的快照，不代表外部副作用完成。入队时立即序列化，之后修改原对象不会改变意图。它不发起网络调用，也不保存或提交事务。必须保存同一作用域的工作单元；显式事务还需要 CommitAsync。未保存作用域的释放或事务回滚，会连同业务改动一起丢弃意图。

以当前持久化租户为准，显式指定不一致租户会被拒绝。规范化租户和生产者追踪随显式 Headers 保存；不会复制当前身份、访问令牌、Claims 或任意 Context.Items。Headers 是持久化数据，调用方应明确选择可保存的值，不应复制整个请求头。每次尝试恢复新的 Context 并创建独立 DI 作用域，不模拟原请求用户。

第三个可选参数是 `IReadOnlyDictionary<string, string?> headers`。入队时立即复制并序列化字典，保留 null 值；每次尝试通过 `OutboxDeliveryContext.Headers` 提供只读快照。键按大小写不敏感处理。传入框架管理的 `X-Message-Id`、`X-Tenant-Id` 或 `traceparent` 会在暂存前报错。MessageDelivery 复制自定义头，再写入稳定消息 ID、当前持久化租户与本次追踪。

```csharp
var messageId = await outbox.EnqueueAsync(message, invocation.Context,
    headers: new Dictionary<string, string?> { ["correlation-id"] = correlationId },
    cancellationToken: ct);
```

NOF 不保证发件或收件顺序。删除 OutboxEnqueueOptions、序号分配、顺序头常量、流租约、等待缺口、完成标记与失败阻塞。旧顺序头只作为普通数据传递。CreatedAtUtc/Id 排序仅用于批次调度；并发 Worker、重试和多队列都可能改变投递顺序。某条消息永久失败不会阻塞其他消息。业务版本放在消息数据中。需要顺序时，由基础设施负责持久化、领取调度、发送/队列并发与消费恢复；仅加 Header 或让单个发布者串行调用，不能保证端到端顺序。

## 基础设施注册与处理器

```csharp
builder.AddApplicationPart(typeof(SendOrderConfirmationHandler).Assembly);
builder.Services.AddOutbox();

public sealed class SendOrderConfirmationHandler(MailClient client)
    : OutboxHandler<SendOrderConfirmation>
{
    public const string MessageType = "orders.send-confirmation";

    public override async ValueTask HandleAsync(
        SendOrderConfirmation message, OutboxDeliveryContext context, CancellationToken ct)
    {
        // MailClient 是应用自己的具体客户端，拥有明确的接受条件。
        await client.SendConfirmationAsync(message.Email, message.OrderId,
            idempotencyKey: context.MessageId.ToString(), cancellationToken: ct);
    }
}
```

每个具体 OutboxHandler<TMessage> 只处理一种消息。Infrastructure 生成器发现程序集内可访问的非泛型 Handler，通过 AddApplicationPart 注册 Scoped 实现与强类型投递委托，不需要 Attribute 或 partial。Handler 必须声明 public const string MessageType；可选的正整数 public const int SchemaVersion 默认是 1。这些常量描述持久化格式，与队列和 CLR 名称无关。无效声明报告 NOF330，同程序集消息类型或标识/版本重复报告 NOF331，跨程序集冲突在宿主装配时失败。注册不构造 Handler，也不启动 Worker；宿主在添加 ApplicationPart 之前或之后显式调用 AddOutbox() 启用能力。开放泛型模板不自动发现；需要手动选择闭合适配器时，可使用 AddOutboxHandler<TMessage, THandler>(id, version)。

迁移时将 IOutboxHandler<T> 改成 OutboxHandler<T>，为 HandleAsync 添加 override，拆开多消息实现，并将原有标识和版本原样移入常量。用 AddApplicationPart 和无参 AddOutbox() 替换原注册回调，移除 OutboxBuilder。持久化记录格式不变。

OutboxDeliveryContext 提供 MessageId、Context、Attempt 和 Headers。它是投递元数据，不是 InvocationContext；意图不一定执行 NOF 操作。处理器正常返回前，应达到具体客户端约定的接受条件。可重试异常遵循 MaxRetryCount 与可选 RetryDelay；OutboxPermanentFailureException 表示无法通过重试恢复，立即终止。未知标识/版本和与注册 JSON 结构不符的内容也永久失败，不根据持久化 CLR 名称加载类型或猜测适配器。

一个 CLR 消息类型和一个稳定的 (MessageType, SchemaVersion) 在每个宿主都只能绑定一个处理器。注册表属于 DI，首次使用即冻结。需要独立重试的渠道使用不同意图类型。升级结构时保留旧版本注册，或显式迁移存量记录。Native AOT 中通过 AddNOFJson 的宿主 Options 注册消息的源生成 JsonSerializerContext；注册表使用静态强类型委托和 IObjectSerializer，无需 CLR 名称查找。

## 具体客户端与入站绑定

```csharp
// 即时等待传输接受，调用方明确指定实际队列/主题。
await memoryClient.SendAsync("mail.send.v1", message, context, ct);
await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, payload, ct);

// Outbox 适配器通过此重载传递原始 ID 和自定义 Headers。
await memoryClient.SendAsync("mail.send.v1", message, deliveryContext, ct);

// 只负责入站：物理拓扑与 Outbox 逻辑消息结构分开。
services.AddMemoryMessaging(endpoints =>
    endpoints.MapQueue<MailService.Send>("mail.send.v1"));
```

AddNOF 注册 MemoryMessageClient；AddRabbitMQ 提供原生连接访问和可选队列消费者，只有 ConfigureRabbitMQ 映射队列端点才启用 RabbitMQ 入站消费者。适配器自己的队列/订阅端点仍进入统一执行策略，并拒绝非 Empty 或声明了失败的操作。移除出站查找后，同一操作可以接多个明确队列。RouteToQueue、Topic 与 GetQueue/GetTopic API 均删除。

示例 SubmitDemoOrder 处理器直接暂存 SendEmail 和 OrderCreated，最后保存同一工作单元一次。NOF.Sample 宿主适配器选择原生 RabbitMQ 目的地，NOF.Sample.Notifications 将接收队列映射到操作。见[双服务手动路由示例](rabbitmq-sample.md)。更换投递方式只修改基础设施，无需改变 Application 入队 API。

## 可靠处理与迁移

记录使用稳定字符串 MessageType、SchemaVersion、Payload、显式暂存的 Headers、TraceParent、状态/次数/租约、NextAttemptAtUtc。删除 DispatchRoutes 与 OutboxMessageType。领取时排除延迟重试和有效租约；完成、失败、释放都检查领取者，过期工作者不能覆盖新领取者。停机时，已开始且结果不确定的尝试保留租约等待恢复；未开始的记录释放且不消耗尝试次数。

投递是至少一次。进程可能在外部副作用完成后、写回完成状态前退出；重试始终复用 MessageId，具体客户端与外部系统须实现选定的幂等语义。同一处理器调用多个系统可能部分成功，需要独立恢复时拆成多条意图。工作者不会自动提交处理器内部的业务改动。

破坏性升级步骤：

1. 停止旧版本生产者，排空旧 Outbox，并显式处理失败记录。旧工作者不能与新结构混用。
2. 备份宿主及所有租户数据库，注册新意图标识/版本、JSON 元数据和处理器。
3. 为每个数据库应用对应 Provider 迁移。PostgreSQL 示例 20261113130000_GenericOutbox 会锁表，并在任何旧记录不是 Sent 时拒绝升级，再替换旧类型/路由字段。历史完成记录标为 legacy.completed、版本 1，不会重新投递；不会自动解释未完成旧负载。
4. 部署一致的运行时/Provider/宿主包后恢复生产者。DeferSend/DeferPublish 及顺序变体改为 EnqueueAsync 加可选 Headers，即时分派改为具体客户端。NOFTestScope.EnqueueAsync 在当前作用域暂存，需显式保存该作用域；测试宿主的即时 Send/Publish 包装已删除。
5. 回滚时一起恢复备份和旧包。因为无法推断意图对应的旧路由，自动 Down 被拒绝。旧程序不能打开新结构。

已经使用泛型意图的宿主：停止所有生产者和 Worker，排空或显式迁移尚未完成的顺序 Outbox/Inbox 消息到基础设施实现，处理被阻塞的序号，并备份每个租户数据库。在 GenericOutbox 之后应用 `20261114090000_RemoveMessageOrdering`。迁移锁定两张消息表和两张顺序状态表，遇到未完成的顺序消息或阻塞的收件状态时拒绝执行；随后删除两张顺序表、相关索引与消息表各三个顺序列。ID、负载、Headers、状态、重试信息和去重历史保持不变，包括未完成的普通消息。如需审计，应先归档旧顺序状态。历史迁移保持原样，不猜测如何将旧序号转换为 Header。回退需恢复匹配的备份和程序；Down 拒绝重建已丢失的状态。自定义 Store 需从 ReceiveAsync/领取逻辑移除顺序参数，CleanupAsync 只返回删除数量 int。

这里只提供迁移代码，真实 PostgreSQL 数据库应用属于部署步骤。SQLite 回归验证共享事务存储、独立领取和领取者隔离；其他测试覆盖快照/回滚、重试 ID/作用域、独立订阅、租户隔离、取消恢复与禁用反射后的强类型投递。消息确认条件由具体客户端保证。可选的 [RabbitMQ 示例集成测试](rabbitmq-sample.md) 验证真实队列、事件分发和离线恢复，不负责 PostgreSQL 部署验证。

## 按能力装配与独立 Worker 宿主

仅 AddNOF 不注册 IOutbox、可靠 Inbox/Outbox Store、对应模型贡献器或处理/清理 Worker。AddOutbox 完整装配 Outbox 能力，AddInbox 完整装配传输无关的 Inbox 能力。AddMemoryMessaging 和配置队列端点的 ConfigureRabbitMQ 自动调用 AddInbox；只注册客户端的 AddRabbitMQ 不启用消费者或 Inbox Worker。基础设施注册幂等，尊重自定义 Store/Provider，在 AddNOF 前后调用都有效；重复消息/Schema 声明仍然报错。调度参数只在对应能力启用且运行 Worker 时验证。

```csharp
builder.AddApplicationPart(typeof(DeliverOrderCreated).Assembly);
builder.Services.AddOutbox();
builder.Services.Configure<TransactionalMessageOptions>(options => options.Outbox.RunWorkers = false);
```

仅生产者宿主仍可暂存快照、提交工作单元，但不认领、发送、重试或清理消息。Inbox.RunWorkers=false 同样接收持久化回执，但不处理/清理。独立 Worker 宿主使用相同 Schema/绑定启用对应能力，并配置正确持久化与传输。关闭 Worker 不丢弃待处理记录，也不重置所有权。本次能力开关无需新增数据库迁移；从旧版本升级仍须遵循既有 generic-outbox 迁移要求。

自定义 IOutboxStore 在没有 ORM/传输、开启 DI 依赖验证时仍可使用。AddNOF 注册核心执行能力；宿主显式选择可选能力与具体 Provider。

能力配置不会修改既有模型快照或执行删表。共享数据库的迁移宿主应保留该库完整的能力模型，不要为了关闭某个进程的 Worker 生成破坏性删表迁移；RunWorkers=false 保留模型及持久化进度。

端点映射现由具体传输适配器负责。调整已有 Inbox 回执路由前，参见[适配器边界与回执键迁移](rabbitmq-sample.md#适配器边界与迁移)。

Inbox 回执只按 ID 与不透明 Route 标识，删除未使用的 Command/Notification 标签和 Memory Rider 包装。分类列迁移及原生持久化配置见[持久化边界](persistence-boundaries.md)。默认内存后端只保存消息状态。
