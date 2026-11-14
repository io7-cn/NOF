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

在 Dashboard 中停止 `sample-notifications`，再次提交，再重启接收服务，查询回执即可观察补收。持久订阅至少建立一次后，离线期间的消息会保留在 RabbitMQ。全新订阅无法收到其队列创建前发布的事件，因此 AppHost 首次启动时先等待接收服务，避免这类竞态。

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

`NOF.Sample.Messaging/MessageOutboxHandlers.cs` 中的两个适配器分别选择 RabbitMQ 客户端和物理目的地：

```csharp
await client.SendAsync(RabbitMQDemoRoutes.MailQueue, message, deliveryContext, ct);
await client.PublishAsync(RabbitMQDemoRoutes.OrderCreatedTopic, message, deliveryContext, ct);
```

这里必须转发完整的 `OutboxDeliveryContext`，以便重试时保留原始消息 ID、追踪和顺序元数据。只转发其语义 `Context` 会创建新的投递 ID。逻辑消息 Schema 与物理队列名各自独立，调整物理目的地无需修改 Application 的入队调用。

接收宿主在 `NotificationMessaging.cs` 明确绑定物理路由与操作：

```csharp
builder.Services.AddRabbitMQ(options => options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq"), endpoints =>
{
    endpoints.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue);
    endpoints.MapSubscription<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderCreatedTopic, RabbitMQDemoRoutes.AuditSubscription);
    endpoints.MapSubscription<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderCreatedTopic, RabbitMQDemoRoutes.MetricsSubscription);
});
```

| 意图 | 物理目的地 | 消费操作 |
|---|---|---|
| `SendEmail` | 队列 `sample.mail.v1` | `SendMail` |
| `OrderCreated` | Topic `sample.orders.created.v1`，订阅 `sample-order-audit` | `RecordOrder` |
| `OrderCreated` | 同一 Topic，订阅 `sample-order-metrics` | `CountOrder` |

队列让多个消费者竞争处理命令；同一 Topic 的不同命名订阅各获得一份事件。使用相同订阅名扩容时是竞争处理，不会增加事件副本。适配器负责声明 direct/fanout exchange 与持久队列。路由常量、绑定、Broker 配置、客户端都在基础设施层；Contract 只声明负载和操作，Application 只实现处理器和回执读写端口。

两个宿主都通过 `AddRabbitMQ` 使用 Aspire 注入的 `rabbitmq` 连接字符串。接收者通过配置 RabbitMQ 端点开启 Inbox。生产者不配置消费者；它在 Outbox 之外保留停用的 Inbox 能力（`AddInbox` 加 `Inbox.RunWorkers=false`），仅用于保持已有 PostgreSQL 迁移模型一致。接收处理器通过 `INotificationReceipts` 暂存投影，不独立保存；Inbox Worker 在同一 SQLite 工作单元中提交业务记录和处理状态。Broker 的 ACK 表示消息已持久化进入 Inbox，并不表示业务处理器已完成。

## 持久化与验证

接收服务在项目工作目录使用 `notifications.db`。NOF 首次使用时初始化全新 SQLite 结构，此演示库没有迁移链；重置时先停服务再删除该数据库。生产环境应制定明确迁移策略。生产者仍遵循现有 PostgreSQL 迁移及升级注意事项，本次没有增加迁移或自动清理已有数据。新增 `SendEmail.OrderId` 为可选项，原 v1 负载仍可接收。

投递语义是至少一次。NOF 保留消息 ID，通过 Inbox 按路由去重；事件的两个订阅刻意使用不同 Inbox 路由，因此相同 ID 会分别处理。真实 SMTP 等外部副作用仍需要自己的幂等方案。

普通测试在未配置 `NOF_SAMPLE_RABBITMQ` 时跳过真实 Broker 测试。测试会声明示例的固定持久路由名，应使用独立 Broker 或 vhost。将该环境变量设为连接字符串后执行：

```sh
dotnet test sample-tests/NOF.Sample.Tests -c Release --filter FullyQualifiedName~RabbitMQDispatchTests
```

测试使用独立生产/接收宿主与数据库，验证真实 RabbitMQ 队列、事件分发、接收端离线与重启补收，并比对 Inbox 与 Outbox ID。显式配置的 Broker 不可用时测试失败。CI 在独立 Job 中提供临时 RabbitMQ 执行验证。真实 PostgreSQL 升级仍属于单独部署验证。

## 适配器边界与回执键迁移

删除 MessageBinding、MessageBindingRegistry 和 AddMessageBindings。RabbitMQ 的端点注册表属于 RabbitMQ 包，Memory 有独立端点注册表。通用 Inbox 不查找队列、Topic 或订阅，只把回执 Route 当作不透明标识，通过原生 keyed DI 解析 scoped `IInboxHandler`，在现有租户作用域和事务中处理。`OperationInboxHandler` 经统一授权和 Filter 边界调用已选定操作。自定义适配器可以直接注册处理器，无需操作或拓扑注册表。

新 Inbox 键包含传输：`rabbitmq:queue:<queue>`、`rabbitmq:subscription:<subscription>`，内存端使用对应 `memory:` 前缀。Broker 物理队列/Exchange 名和 Outbox Schema 标识不变。升级前停止并排空旧消费者和 Inbox 工作，备份 Inbox 与顺序状态，保留去重历史。旧 `queue:`/`subscription:` 回执和顺序状态的路由需要显式映射到选定传输，在停机期间同步更新 Inbox.Route 和 InboxOrderState.Route。保留消息 ID、顺序进度和已完成历史，先处理键冲突及竞争消费者部署再恢复。本次不自动重映射、删数据或执行表迁移；缺少映射后的历史直接重放已处理消息，可能重复业务副作用。
