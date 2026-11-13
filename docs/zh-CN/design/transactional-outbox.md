# 通用事务性发件箱

[English](../../en/design/transactional-outbox.md) · [统一操作](unified-operations.md) · [持久化边界](persistence-boundaries.md)

Application 通过 IOutbox 暂存可靠执行意图。即时调用直接使用具体 HTTP 客户端、SDK、MemoryMessageClient 或 RabbitMQMessageClient。基础设施定义每种意图如何处理，以及什么条件代表投递成功。移除 IMessageDispatcher、出站操作路由、ICommandRider/INotificationRider 接口，以及 Outbox 引擎中的 Command/Notification 分支。

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

以当前持久化租户为准，显式指定不一致租户会被拒绝。只持久化规范化租户和生产者追踪信息，不重放访问令牌、Claims 或任意 Context.Items。每次尝试恢复新的 Context 并创建独立 DI 作用域，不模拟原请求用户。

顺序流可传入 new OutboxEnqueueOptions { OrderKey = "order:42", CompletesOrderKey = false }，最后一条设 CompletesOrderKey 为 true。沿用服务限定、租户持久化隔离和连续序列分配；后续记录不能越过未完成或永久失败的前驱。独立流继续处理。失败流需要显式恢复，框架不会自动跳过失败意图。

## 基础设施注册与处理器

```csharp
services.AddOutbox(outbox => outbox
    .Add<SendOrderConfirmation, SendOrderConfirmationHandler>(
        messageType: "orders.send-confirmation", schemaVersion: 1));

public sealed class SendOrderConfirmationHandler(MailClient client)
    : IOutboxHandler<SendOrderConfirmation>
{
    public async ValueTask HandleAsync(
        SendOrderConfirmation message, OutboxDeliveryContext context, CancellationToken ct)
    {
        // MailClient 是应用自己的具体客户端，拥有明确的接受条件。
        await client.SendConfirmationAsync(message.Email, message.OrderId,
            idempotencyKey: context.MessageId.ToString(), cancellationToken: ct);
    }
}
```

OutboxDeliveryContext 提供 MessageId、Context、Attempt、OrderKey、Sequence 和 CompletesOrderKey。它是投递元数据，不是 InvocationContext；意图不一定执行 NOF 操作。处理器正常返回前，应达到具体客户端约定的接受条件。可重试异常遵循 MaxRetryCount 与可选 RetryDelay；OutboxPermanentFailureException 表示无法通过重试恢复，立即终止。未知标识/版本和与注册 JSON 结构不符的内容也永久失败，不根据持久化 CLR 名称加载类型或猜测适配器。

一个 CLR 消息类型和一个稳定的 (MessageType, SchemaVersion) 在每个宿主都只能绑定一个处理器。注册表属于 DI，首次使用即冻结。需要独立重试的渠道使用不同意图类型。升级结构时保留旧版本注册，或显式迁移存量记录。Native AOT 中通过 JsonSerializerOptions.NOF 注册消息的源生成 JsonSerializerContext；注册表使用静态强类型委托和 IObjectSerializer，无需 CLR 名称查找。

## 具体客户端与入站绑定

```csharp
// 即时等待传输接受，调用方明确指定实际队列/主题。
await memoryClient.SendAsync("mail.send.v1", message, context, ct);
await rabbitClient.PublishAsync("orders.created.v1", orderCreated, context, ct);

// Outbox 适配器通过此重载传递原始 ID 和顺序元数据。
await memoryClient.SendAsync("mail.send.v1", message, deliveryContext, ct);

// 只负责入站：物理拓扑与 Outbox 逻辑消息结构分开。
services.AddMessageBindings(bindings =>
    bindings.Queue<SendEmail>("mail.send.v1").To(MailService.Send.Descriptor));
```

AddNOF 注册 MemoryMessageClient；AddRabbitMQ 额外注册 RabbitMQMessageClient，不再静默替换内存客户端，并继续配置 RabbitMQ 入站消费者。Queue/Subscription 仍进入统一执行策略，并拒绝非 Empty 或声明了失败的操作。移除出站查找后，同一操作可以接多个明确队列。RouteToQueue、Topic 与 GetQueue/GetTopic API 均删除。

示例 TransactionalMessages 暂存 SendEmail 和 OrderCreated；宿主的 SendEmailOutboxHandler、OrderCreatedOutboxHandler 选择内存客户端与实际路由，再由现有邮件、订单操作消费。换成消息代理、HTTP 或 SDK，只修改基础设施处理器，无需改变 Application 入队 API。

## 可靠处理与迁移

记录使用稳定字符串 MessageType、SchemaVersion、Payload、白名单 Headers、TraceParent、状态/次数/租约、NextAttemptAtUtc 以及可选顺序字段。删除 DispatchRoutes 与 OutboxMessageType。领取时排除延迟重试和被前驱阻塞的记录；完成、失败、释放都检查领取者，过期工作者不能覆盖新领取者。停机时，已开始且结果不确定的尝试保留租约等待恢复；未开始的记录释放且不消耗尝试次数。

投递是至少一次。进程可能在外部副作用完成后、写回完成状态前退出；重试始终复用 MessageId，具体客户端与外部系统须实现选定的幂等语义。同一处理器调用多个系统可能部分成功，需要独立恢复时拆成多条意图。工作者不会自动提交处理器内部的业务改动。

破坏性升级步骤：

1. 停止旧版本生产者，排空旧 Outbox，并显式处理失败记录。旧工作者不能与新结构混用。
2. 备份宿主及所有租户数据库，注册新意图标识/版本、JSON 元数据和处理器。
3. 为每个数据库应用对应 Provider 迁移。PostgreSQL 示例 20261113130000_GenericOutbox 会锁表，并在任何旧记录不是 Sent 时拒绝升级，再替换旧类型/路由字段。历史完成记录标为 legacy.completed、版本 1，不会重新投递；不会自动解释未完成旧负载。
4. 部署一致的运行时/Provider/宿主包后恢复生产者。DeferSend/DeferPublish 及顺序变体改为 EnqueueAsync 加选项，即时分派改为具体客户端。NOFTestScope.EnqueueAsync 在当前作用域暂存，需显式保存该作用域；测试宿主的即时 Send/Publish 包装已删除。
5. 回滚时一起恢复备份和旧包。因为无法推断意图对应的旧路由，自动 Down 被拒绝。旧程序不能打开新结构。

这里只提供迁移代码，真实 PostgreSQL 数据库应用属于部署步骤。SQLite 回归验证共享事务存储、顺序阻塞和领取者隔离；其他测试覆盖快照/回滚、重试 ID/作用域、独立订阅、租户隔离、取消恢复与禁用反射后的强类型投递。本地验证不启动真实 Broker 或 PostgreSQL，消息确认条件由具体客户端保证。

## 按能力装配与独立 Worker 宿主

仅 AddNOF 不注册 IOutbox、可靠 Inbox/Outbox Store、对应模型贡献器或处理/清理 Worker。AddOutbox 完整装配 Outbox 能力，AddMessageBindings 完整装配 Inbox 能力。基础设施注册幂等，尊重自定义 Store/Provider，在 AddNOF 前后调用都有效；重复消息/Schema 声明仍然报错。调度参数只在对应能力启用且运行 Worker 时验证。

```csharp
builder.Services.AddOutbox(outbox => outbox.Add<OrderCreated, DeliverOrderCreated>("order.created", 1));
builder.Services.Configure<TransactionalMessageOptions>(options => options.Outbox.RunWorkers = false);
```

仅生产者宿主仍可暂存快照、提交工作单元，但不认领、发送、重试或清理消息。Inbox.RunWorkers=false 同样接收持久化回执，但不处理/清理。独立 Worker 宿主使用相同 Schema/绑定启用对应能力，并配置正确持久化与传输。关闭 Worker 不丢弃待处理记录，也不重置顺序或所有权。本次能力开关无需新增数据库迁移；从旧版本升级仍须遵循既有 generic-outbox 迁移要求。

自定义 IOutboxStore 在没有 ORM/传输、开启 DI 依赖验证时仍可使用；默认有序流分配器仅在实际使用该能力时解析 Provider 持久化。AddNOF 继续提供便捷缓存、序列化、对象存储和原生宿主默认值，不要求用户逐项注册底层服务。

能力配置不会修改既有模型快照或执行删表。共享数据库的迁移宿主应保留该库完整的能力模型，不要为了关闭某个进程的 Worker 生成破坏性删表迁移；RunWorkers=false 保留模型及持久化进度。
