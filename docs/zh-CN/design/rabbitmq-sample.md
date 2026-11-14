# 两个 NOF 服务之间的 RabbitMQ 手动路由

[English](../../en/design/rabbitmq-sample.md) · [事务性 Outbox](transactional-outbox.md)

Aspire 示例现在包含生产者 `NOF.Sample` 和接收者 `NOF.Sample.Notifications`，共同连接 RabbitMQ。AppHost 原本已有 RabbitMQ 资源，现在示例消息真正经过 Broker，不再用进程内 MemoryMessageClient。生产者保留 PostgreSQL、Redis、UI 和 OIDC；接收者独立使用 SQLite 保存 Inbox 和可查询的处理回执。

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
builder.Services.AddOutbox(outbox =>
{
    outbox.Add<SendEmail, SendEmailOutboxHandler>("sample.send-email", 1);
    outbox.Add<OrderCreated, OrderCreatedOutboxHandler>("sample.order-created", 1);
});
```

`NOF.Sample.Messaging/MessageOutboxHandlers.cs` 明确选择 Exchange 和 routing key，通过示例自己的 RabbitMQDemoPublisher 调用原生发送：

```csharp
await publisher.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, deliveryContext, ct);
await publisher.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, deliveryContext, ct);
```

示例发送器使用原生 `IChannel.BasicPublishAsync`，通过 `CreateChannelOptions` 开启 publisher confirms/tracking，发布时指定 `mandatory: true`。`MessageDelivery.ExecuteAsync(..., deliveryContext)` 保留原 Outbox ID、租户/追踪和顺序 Header，BasicProperties.MessageId 同时写入原 ID。Return/Nack 异常直接交给 Outbox 重试。发送时不自动声明任何 Exchange、队列或转发拓扑。

接收宿主在 `NotificationMessaging.cs` 明确绑定物理路由与操作：

```csharp
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connectionString));

// Separate feature modules can append configuration before Build().
builder.Services.ConfigureRabbitMQ(rabbit => rabbit.Topology(RabbitMQDemoTopology.DeclareAsync));
builder.Services.ConfigureRabbitMQ(rabbit => rabbit
    .MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue)
    .MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue)
    .MapQueue<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue));
```

| 意图 | 物理目的地 | 消费操作 |
|---|---|---|
| `SendEmail` | 队列 `sample.mail.v1` | `SendMail` |
| `OrderCreated` | 队列 `sample.order-audit.v1` | `RecordOrder` |
| `OrderCreated` | 队列 `sample.order-metrics.v1` | `CountOrder` |

队列中的多个消费者竞争处理。`RabbitMQDemoTopology` 现在只是普通静态声明方法，ConfigureRabbitMQ 负责启动时序，用户无需实现 IHostedLifecycleService。它用 RabbitMQ.Client 声明持久的 `sample.orders.v1` Topic Exchange 和三个队列，再以 `order.created` 为 routing key 将两个订单队列绑定到 Exchange。邮件通过默认 Exchange（`exchange: ""`）发布，以邮件队列名作为 routing key。NOF 不解释这些拓扑关系。

```csharp
await channel.ExchangeDeclareAsync(RabbitMQDemoRoutes.OrderEventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
await channel.QueueBindAsync(queue, RabbitMQDemoRoutes.OrderEventsExchange, "order.created", cancellationToken: ct);
```

两个绑定的订单队列各收到一份消息；扩容同一队列的消费者只会竞争处理。Contract/Application 仍保持传输无关，路由、拓扑、序列化和原生发布属性都属于示例基础设施。

两个宿主都通过 `AddRabbitMQ` 使用 Aspire 注入的 `rabbitmq` 连接字符串。接收者通过配置 RabbitMQ 端点开启 Inbox。生产者不配置消费者；它在 Outbox 之外保留停用的 Inbox 能力（`AddInbox` 加 `Inbox.RunWorkers=false`），仅用于保持已有 PostgreSQL 迁移模型一致。接收处理器通过 `INotificationReceipts` 暂存投影，不独立保存；Inbox Worker 在同一 SQLite 工作单元中提交业务记录和处理状态。Broker 的 ACK 表示消息已持久化进入 Inbox，并不表示业务处理器已完成。

## 持久化与验证

接收服务在项目工作目录使用 `notifications.db`。NOF 首次使用时初始化全新 SQLite 结构，此演示库没有迁移链；重置时先停服务再删除该数据库。生产环境应制定明确迁移策略。生产者仍遵循现有 PostgreSQL 迁移及升级注意事项，本次没有增加迁移或自动清理已有数据。新增 `SendEmail.OrderId` 为可选项，原 v1 负载仍可接收。

投递语义是至少一次。NOF 保留消息 ID，通过 Inbox 按路由去重；事件的两个订阅刻意使用不同 Inbox 路由，因此相同 ID 会分别处理。真实 SMTP 等外部副作用仍需要自己的幂等方案。

普通测试在未配置 `NOF_SAMPLE_RABBITMQ` 时跳过真实 Broker 测试。测试会声明示例的固定持久路由名，应使用独立 Broker 或 vhost。将该环境变量设为连接字符串后执行：

```sh
dotnet test sample-tests/NOF.Sample.Tests -c Release --filter FullyQualifiedName~RabbitMQDispatchTests
```

测试使用独立生产/接收宿主与数据库，验证真实 RabbitMQ 队列、事件分发、接收端离线与重启补收，并比对 Inbox 与 Outbox ID。显式配置的 Broker 不可用时测试失败。CI 在独立 Job 中提供临时 RabbitMQ 执行验证。真实 PostgreSQL 升级仍属于单独部署验证。

## 适配器边界与迁移

删除 MessageBinding、MessageBindingRegistry 和 AddMessageBindings。RabbitMQ 只提供 MapQueue<Operation>：没有 Topic/Subscription DSL，不自动声明 Exchange、队列或绑定，也不安装统一的 Alternate/Dead-letter 转发拓扑或原始路由重放 Header。RabbitMQMessageClient 和命令/通知 Rider 均移除，发送直接使用原生 Channel；publisher confirms 直接使用 CreateChannelOptions，不再重复提供 NOF 配置。队列不存在则启动失败，mandatory 消息无法路由则投递失败；Broker Policy、DLX 重试、Exchange 绑定及重放由应用或运维负责。

RabbitMQOptions 和 RabbitMQConnectionManager 已删除。AddRabbitMQ 接受原生 ConnectionFactory 配置，使用 AMQP/AMQPS URI（factory.Uri）或原生字段，不再解析旧的 key/value ConnectionString。TLS、超时及自动恢复直接使用原生设置。RabbitMQConnection.GetAsync 只负责惰性共享连接的生命周期；调用方创建和释放原生 Channel，不释放共享连接。首次连接失败允许重试；单个等待者取消不影响其他调用方；已建立连接交由原生机制恢复。

ConfigureRabbitMQ 独立、支持 Fluent API，可在 Build 前多次调用。拓扑回调按声明顺序追加，队列标识必须唯一，启动时冻结完整快照。即使先配置端点、后由另一个模块补充拓扑，框架 StartingAsync 仍会先等待全部拓扑回调，再启动消费者。拓扑失败或取消将中止启动，不自动回滚 Broker 修改；消费者启动失败会释放已打开的 Channel。只配置拓扑不启用 Inbox 存储、模型或 Worker，空配置不触发网络调用；仅 AddRabbitMQ 仍为惰性连接，不注册生命周期或可靠消息 Worker。

MapQueue 可指定 `configureChannel: async (channel, ct) => await channel.BasicQosAsync(0, 8, false, ct)` 和 `requeueOnFailure`；默认 prefetch 为 1，回执持久化失败时重新入队。设置属于当前端点。

通用 Inbox 仍把 Route 当作不透明键，通过原生 keyed DI 解析 scoped IInboxHandler，在现有租户作用域和事务内持久化、去重和执行，不理解拓扑。RabbitMQ 回执键为 `rabbitmq:queue:<queue>`；Memory 使用独立端点和键。优先使用原生 GUID MessageId，缺失时回退到 NOF Header。

升级前停止并排空旧消费者和 Inbox 工作，备份数据。旧 `queue:`/`subscription:`（包括上一版 `rabbitmq:subscription:`）回执键需要显式映射到选定队列键，同步修改 Inbox.Route 与 InboxOrderState.Route，保留 ID、已完成去重历史和顺序进度。停用旧拓扑前，先排空或明确搬运旧订阅队列到新订单队列。已有队列可能带 NOF 安装的死信参数，应比较声明并在停机期间通过原生 RabbitMQ 配置有意保留或重建。NOF 不自动重映射、删数据、重放或修复拓扑。Outbox Schema 仍为 v1，不自动解释旧负载。
