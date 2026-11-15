# 两个 NOF 服务之间的 RabbitMQ 手动路由

[English](../../en/design/rabbitmq-sample.md) · [事务性 Outbox](transactional-outbox.md)

Aspire 示例现在包含生产者 `NOF.Sample` 和接收者 `NOF.Sample.Notifications`，共同连接 RabbitMQ。AppHost 原本已有 RabbitMQ 资源，现在示例消息真正经过 Broker，不再用进程内 MemoryMessageClient。生产者保留 PostgreSQL、Redis、UI 和 OIDC；接收者独立使用 SQLite 保存业务幂等投影与查询数据。

## 运行与观察

在仓库根目录执行，确保 Docker 正在运行且已安装固定版本的 .NET SDK：

```sh
dotnet run --project sample/NOF.Sample.AppHost
```

AppHost 先启动 RabbitMQ 和接收服务，再启动生产者。在 Aspire Dashboard 中找到 `NOF-Sample`、`sample-notifications` 的 HTTP 地址，分别设置为 `PRODUCER`、`RECEIVER`，端口可能不同。

```sh
curl -i -X POST "$PRODUCER/api/messages/orders" \
  -H 'Content-Type: application/json' -d '{"email":"demo@example.test"}'
# 202 Accepted
# {"orderId":"<generated-guid>"}

curl "$RECEIVER/api/messages/receipts?orderId=<generated-guid>"
```

轮询第二个端点，直到 `items` 出现三条回执：邮件（kind 0）、订单审计（kind 1）、订单统计（kind 2）。不传 `orderId` 返回最近 100 条。无效邮件地址返回 400，不暂存消息。此演示允许匿名、宿主租户请求；它模拟提交订单和邮件处理，不保存订单聚合，也不会真正发邮件。

在 Dashboard 中停止 `sample-notifications`，再次提交，再重启接收服务，查询回执即可观察补收。原生持久队列与 Exchange 绑定至少建立一次后，离线期间的消息会保留在 RabbitMQ。全新队列无法收到其绑定建立前发布的事件，因此 AppHost 首次启动时先等待接收服务，避免这类竞态。

## 基础设施层手动路由

Application 只在同一个 `IUnitOfWork` 暂存两种传输无关的意图，最后保存一次：

```csharp
await outbox.EnqueueAsync(new OrderCreated(orderId), context, cancellationToken: ct);
await outbox.EnqueueAsync(new SendEmail(email, "Created", "Order created.", OrderId: orderId), context, cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

生产者明确选择基础设施适配器，以及稳定的逻辑消息标识和版本：

```csharp
builder.Services.AddScoped<IMessageClient, RabbitMQMessageClient>();
builder.AddApplicationPart(typeof(SendEmailOutboxHandler).Assembly);
builder.Services.AddOutbox();
```

`NOF.Sample/Services/SendEmailOutboxHandler.cs` 与 `OrderCreatedOutboxHandler.cs` 分别继承 OutboxHandler<TMessage>，无需 MessageType/SchemaVersion 元数据，通过 ApplicationPart 自动注册。每个 Handler 直接转发已准备好的 OutboxDeliveryContext.Headers，再经 Application.IMessageClient 选择目的地。宿主在 Services/RabbitMQMessageClient.cs 实现该接口，使用原生 Channel 和发布确认：

```csharp
await client.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, headers, ct);
await client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, headers, ct);
```

宿主客户端实现使用原生 `IChannel.BasicPublishAsync`，通过 `CreateChannelOptions` 开启 publisher confirms/tracking，发布时指定 `mandatory: true`。OutboxHandler 直接转发完整 `deliveryContext.Headers`，其中已含 JSON 上下文、原 Outbox ID、租户/追踪和自定义 Headers，BasicProperties.MessageId 同时写入原 ID。Return/Nack 异常直接交给 Outbox 重试。发送时不自动声明任何 Exchange、队列或转发拓扑。

接收宿主在 `NotificationMessaging.cs` 明确绑定物理路由与操作：

```csharp
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connectionString))
    .ConfigureRabbitMQ(NotificationMessaging.Configure);

// The feature module inlines its native topology and endpoint declarations.

// NotificationMessaging.Configure:
rabbit.Topology(async (channel, ct) =>
{
    // Native ExchangeDeclareAsync, QueueDeclareAsync and QueueBindAsync.
})
.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue)
.MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue)
.MapQueue<CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue);
```

| 意图 | 物理目的地 | 消费操作 |
|---|---|---|
| `SendEmail` | 队列 `sample.mail.v1` | `SendMail` |
| `OrderCreated` | 队列 `sample.order-audit.v1` | `RecordOrder` |
| `OrderCreated` | 队列 `sample.order-metrics.v1` | `CountOrder` |

队列中的多个消费者竞争处理。`NotificationMessaging.Configure` 直接内联原生拓扑 lambda 和队列映射，ConfigureRabbitMQ 负责启动时序，用户无需实现 IHostedLifecycleService 或单独的拓扑持有类。它用 RabbitMQ.Client 声明持久的 `sample.orders.v1` Topic Exchange 和三个队列，再以 `order.created` 为 routing key 将两个订单队列绑定到 Exchange。邮件通过默认 Exchange（`exchange: ""`）发布，以邮件队列名作为 routing key。NOF 不解释这些拓扑关系。

```csharp
await channel.ExchangeDeclareAsync(RabbitMQDemoRoutes.OrderEventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
await channel.QueueBindAsync(queue, RabbitMQDemoRoutes.OrderEventsExchange, "order.created", cancellationToken: ct);
```

两个绑定的订单队列各收到一份消息；扩容同一队列的消费者只会竞争处理。Contract/Application 仍保持传输无关，路由、拓扑、序列化和原生发布属性都属于示例基础设施。

两个宿主都通过 `AddRabbitMQ` 使用 Aspire 注入的 `rabbitmq` 连接字符串。接收者的队列映射自动装配事务性消息，并通过 `Outbox.RunWorkers=false` 停止发件 Worker。生产者调用 `AddOutbox`，通过 `Inbox.RunWorkers=false` 停止收件 Worker，不配置消费者。两侧均保留完整消息模型，包括生产者已有的 PostgreSQL 迁移模型。接收处理器通过 `INotificationReceipts` 暂存投影，不独立保存；Inbox Worker 在同一 SQLite 工作单元中提交业务记录和处理状态。Broker 的 ACK 表示消息已持久化进入 Inbox，并不表示业务处理器已完成。

## 持久化与验证

生产者选择原生 EF/PostgreSQL，调用 AddOutbox，在同一业务工作单元中保存两条意图。接收者选择 EF/SQLite 仅用于业务投影，MapQueue 不安装 Outbox/Inbox 持久化。操作直接等待 INotificationReceipts.RecordAsync；该端口使用参数化 SQLite INSERT ... ON CONFLICT DO NOTHING，依赖 按租户隔离的 (IdempotencyKey, Kind, TenantId) 唯一索引。订单 ID 是业务键；无订单的邮件使用稳定消息 ID。同一订单即使有不同传输 ID，每种投影也只生成一次，不使用先查询后插入。

RabbitMQ 在操作的业务投影提交后 ACK，不再确认收件入库。提交与 ACK 之间失败会重投，唯一业务键使投影保持不变。邮件操作仅用投影模拟发送，真实 SMTP/服务商副作用需要 Outbox 或服务商幂等约定，单独记录一行不能让外部发送具有事务性。

无需 Broker 的 NotificationIdempotencyTests 并发交付重复消息，验证恰好三个业务投影且没有框架收件表。RabbitMQDeliveryTests 用模拟 Channel 驱动真实 Consumer 回调，验证执行完成前不 ACK、异常 NACK、非 Guid 标识透传。可选真实 Broker 测试覆盖队列/广播、离线恢复、完整 Header、原生/旧 Header 标识以及业务去重。设置隔离的 NOF_SAMPLE_RABBITMQ 连接串后运行，未设置时跳过；测试不会执行生产 PostgreSQL 迁移。

## 适配器边界与迁移

AddRabbitMQ 配置原生 ConnectionFactory；可重复链式 ConfigureRabbitMQ 在 Build 前追加拓扑回调和队列到操作的映射。框架 StartingAsync 在消费者启动前完成全部拓扑。只有 MapQueue 注册消费者，纯拓扑/客户端宿主无需持久化。队列映射要求 void、拒绝重复队列，不隐式创建拓扑。原生拓扑、预取、Requeue、死信策略属于宿主，Global/Service/UseCase 权限和 Filter 在构造 Handler 前继续生效。

每次交付在新 Scope 恢复 Context，直接执行所选操作。框架没有 InboxEndpointRegistry、收件路由、认领、重试计数或消费数据库。缺少传输 MessageId 就保持缺失，原生 ID 优先于旧 Header，不强制转换 Guid。直接 Memory 分发使用相同执行边界并传播失败，但不承诺 Broker 式跨进程恢复。

切换前停止旧消费者/Inbox Worker，备份租户数据库。使用旧运行时处理 Pending/Failed，或显式导出并按审查方案重放；旧消息可能早已被 Broker ACK。按需导出完成历史。生产者 20261116000000_RemoveInbox 防护迁移拒绝未完成记录，仅删除旧 Inbox 表，保留 Outbox 和身份数据。本次源码修改不会执行线上数据库变更。

## 示例项目边界

NOF.Sample.Notifications 是一个服务项目，包含操作、业务投影端口和原生 SQLite 实现。共享载荷/路由常量位于 NOF.Sample.Contract；发件客户端接口位于 Application，原生实现与 Outbox Handler 位于 NOF.Sample。CountOrder 仍是独立 OperationHandler 表达的内部反应，不声明为 Contract 方法。

NotificationsDbContext 的 EnsureCreated 只创建新本地文件，不升级旧文件。旧接收库需审查 IdempotencyKey 与唯一索引迁移：为已知订单投影推导订单键、显式处理重复记录，并为旧的无订单/消息 ID 邮件决定业务键。删除旧表前备份 Inbox 历史并解决未完成工作，不要通过删除已有文件伪装迁移。示例业务投影是可观测数据，不是重新包装的通用 NOF Inbox。
