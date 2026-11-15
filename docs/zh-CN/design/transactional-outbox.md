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

以当前持久化租户为准，显式不一致租户被拒绝。入队时保存所有 JSON 标量 Context 项、负载、显式 Headers、规范化租户、稳定消息 ID 和入队追踪；不序列化 IUserContext/Claims 或 DI/运行时状态。每次尝试都在构造 Handler 前还原并绑定 Context，使用新的 DI 作用域。

第三个可选参数是 `IReadOnlyDictionary<string, string?> headers`，入队即复制并序列化，保留 null。OutboxDeliveryContext.Headers 只读且可直接发送，已包含 X-Message-Id、X-Tenant-Id、traceparent 和 X-NOF-Context 中的 JSON 快照；这些大小写不敏感的管理键不能被覆盖。原生适配器直接转发，无需 MessageDelivery，该服务已删除。

```csharp
var messageId = await outbox.EnqueueAsync(message, invocation.Context,
    headers: new Dictionary<string, string?> { ["correlation-id"] = correlationId },
    cancellationToken: ct);
```

NOF 不保证发件或收件顺序。删除 OutboxEnqueueOptions、序号分配、顺序头常量、流租约、等待缺口、完成标记与失败阻塞。旧顺序头只作为普通数据传递。CreatedAtUtc/Id 排序仅用于批次调度；并发 Worker、重试和多队列都可能改变投递顺序。某条消息永久失败不会阻塞其他消息。业务版本放在消息数据中。需要顺序时，由基础设施负责持久化、领取调度、发送/队列并发与消费恢复；仅加 Header 或让单个发布者串行调用，不能保证端到端顺序。

## 基础设施注册与处理器

```csharp
builder.AddApplicationPart(typeof(SendOrderConfirmationHandler).Assembly);
builder.Services.AddTransactionalMessages();

public sealed class SendOrderConfirmationHandler(MailClient client)
    : OutboxHandler<SendOrderConfirmation>
{

    public override async ValueTask HandleAsync(
        SendOrderConfirmation message, OutboxDeliveryContext context, CancellationToken ct)
    {
        // MailClient 是应用自己的具体客户端，拥有明确的接受条件。
        await client.SendConfirmationAsync(message.Email, message.OrderId,
            idempotencyKey: context.MessageId.ToString(), cancellationToken: ct);
    }
}
```

每个具体 OutboxHandler<TMessage> 只处理一种消息。Infrastructure 生成器通过 AddApplicationPart 注册可访问、非泛型 Handler 的 Scoped 实现与强类型投递委托。业务只实现 HandleAsync，无需 Attribute、partial、MessageType 或 SchemaVersion。持久化标识自动取 typeof(TMessage).ToString()，包含命名空间与闭合泛型参数，不包含程序集版本；同一宿主必须唯一，并符合现有 512 字符列长度。NOF330 检查不可访问的声明和已移除的元数据成员，NOF331 检查重复消息处理器，跨程序集冲突在装配时拒绝。注册不构造 Handler 或启动 Worker，宿主显式 AddTransactionalMessages() 启用能力。开放泛型模板可通过 AddOutboxHandler<TMessage,THandler>() 手动选择闭合类型，无元数据参数。

推导的标识只查找已注册的强类型委托，不传给 Type.GetType，也不按持久化 CLR 名称动态加载程序集。SchemaVersion 从实体、Store、注册表和分发 API 中删除。处理器改名无需修改注册；消息 DTO 类型或命名空间改名会改变持久化键，必须排空或显式迁移已有记录。不兼容的载荷演进由业务类型及迁移处理，不由框架维护版本分发。

迁移时将 IOutboxHandler<T> 改为 OutboxHandler<T>，拆开多消息适配器，并删除 MessageType/SchemaVersion 声明与注册实参，保留 AddApplicationPart 加 AddTransactionalMessages。停止旧进程、备份各租户数据库，并把旧标识显式转换为经过审查的推导标识。示例 SimplifyOutboxMetadata 仅转换两种未改变的 v1 载荷标识，拒绝非 v1 的待发/失败工作，再删除 SchemaVersion；保留消息 ID、载荷、Header、投递状态及 Inbox 历史。其他应用需提供自己的标识/载荷映射，不增加运行时猜测旧格式的兼容层。已丢失的版本值无法重建，降级需匹配备份。

OutboxDeliveryContext 提供 MessageId、Context、Attempt 和 Headers。它是投递元数据，不是 InvocationContext；意图不一定执行 NOF 操作。处理器正常返回前，应达到具体客户端约定的接受条件。可重试异常遵循 MaxRetryCount 与可选 RetryDelay；OutboxPermanentFailureException 表示无法通过重试恢复，立即终止。未知消息标识和与注册 JSON 结构不符的内容也永久失败，不根据持久化 CLR 名称加载类型或猜测适配器。

每个 CLR 消息类型及推导标识在每个宿主都只能绑定一个处理器。注册表属于 DI，首次使用即冻结。需要独立重试的渠道使用不同意图类型。不兼容载荷升级前需排空或显式迁移存量记录。Native AOT 中通过 AddNOFJson 的宿主 Options 注册消息的源生成 JsonSerializerContext；注册表使用静态强类型委托和 IObjectSerializer，无需 CLR 名称查找。

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

AddMemoryMessaging 注册 MemoryMessageClient；AddRabbitMQ 提供原生连接访问和可选队列消费者，只有 ConfigureRabbitMQ 映射队列端点才启用 RabbitMQ 入站消费者。适配器自己的队列/订阅端点仍进入统一执行策略，并只接受声明 void 且无业务 FailureCase 的操作。移除出站查找后，同一操作可以接多个明确队列。RouteToQueue、Topic 与 GetQueue/GetTopic API 均删除。

示例 SubmitDemoOrder 处理器直接暂存 SendEmail 和 OrderCreated，最后保存同一工作单元一次。NOF.Sample 宿主适配器选择原生 RabbitMQ 目的地，NOF.Sample.Notifications 将接收队列映射到操作。见[双服务手动路由示例](rabbitmq-sample.md)。更换投递方式只修改基础设施，无需改变 Application 入队 API。

## 可靠处理与迁移

记录使用自动推导的字符串 MessageType 与 Payload、显式暂存的 Headers、TraceParent、状态/次数/租约、NextAttemptAtUtc。删除 DispatchRoutes 与 OutboxMessageType。领取时排除延迟重试和有效租约；完成、失败、释放都检查领取者，过期工作者不能覆盖新领取者。停机时，已开始且结果不确定的尝试保留租约等待恢复；未开始的记录释放且不消耗尝试次数。

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

仅 AddNOF 不注册 IOutbox、可靠 Inbox/Outbox Store、对应模型贡献器或处理/清理 Worker。AddTransactionalMessages 统一装配 Inbox 与 Outbox：两侧 Store/注册表、暂存与收件 API、共享持久化及四个处理/清理 Worker。两侧默认都运行，通过 Inbox.RunWorkers 和 Outbox.RunWorkers 分别控制。AddMemoryMessaging 和配置队列端点的 ConfigureRabbitMQ 自动调用 AddTransactionalMessages；只注册客户端的 AddRabbitMQ 不启用消费者或 Inbox Worker。基础设施注册幂等，尊重自定义 Store/Provider，在 AddNOF 前后调用都有效；重复消息/Schema 声明仍然报错。调度参数只在对应能力启用且运行 Worker 时验证。

```csharp
builder.AddApplicationPart(typeof(DeliverOrderCreated).Assembly);
builder.Services.AddTransactionalMessages();
builder.Services.Configure<TransactionalMessageOptions>(options => options.Outbox.RunWorkers = false);
```

仅生产者宿主仍可暂存快照、提交工作单元，但不认领、发送、重试或清理消息。Inbox.RunWorkers=false 同样接收持久化回执，但不处理/清理。独立 Worker 宿主同样使用 AddTransactionalMessages 和相同 Schema/绑定，通过 RunWorkers 控制所需方向，并配置正确持久化与传输。关闭 Worker 不丢弃待处理记录，也不重置所有权。将 AddInbox/AddOutbox 替换为一次 AddTransactionalMessages，不保留兼容别名。原本已映射两张表的宿主无需修改数据库结构；原本只有单侧模型的 EF/NHibernate 宿主现在包含两张表，部署前应审查新增表的迁移。关闭 Worker 保留两侧模型和全部持久化状态；从旧版本升级仍须遵循既有 generic-outbox 迁移要求。

自定义 IOutboxStore 在没有 ORM/传输、开启 DI 依赖验证时仍可使用。AddNOF 注册核心执行能力；宿主显式选择可选能力与具体 Provider。

能力配置不会修改既有模型快照或执行删表。共享数据库的迁移宿主应保留该库完整的能力模型，不要为了关闭某个进程的 Worker 生成破坏性删表迁移；RunWorkers=false 保留模型及持久化进度。

端点映射现由具体传输适配器负责。调整已有 Inbox 回执路由前，参见[适配器边界与回执键迁移](rabbitmq-sample.md#适配器边界与迁移)。

Inbox 回执只按 ID 与不透明 Route 标识，删除未使用的 Command/Notification 标签和 Memory Rider 包装。分类列迁移及原生持久化配置见[持久化边界](persistence-boundaries.md)。默认内存后端只保存消息状态。

JSON Context、可直接发送的投递参数与链式注册参见[JSON 执行上下文](execution-context.md)。
