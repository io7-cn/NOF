# RabbitMQ routing across two NOF services

[简体中文](../../zh-CN/design/rabbitmq-sample.md) · [Transactional outbox](transactional-outbox.md)

The Aspire sample runs a producer (`NOF.Sample`) and a receiver (`NOF.Sample.Notifications`) against the same RabbitMQ resource. RabbitMQ was already declared by AppHost; it now carries the sample messages instead of the in-process memory client. The producer retains PostgreSQL, Redis and the existing UI/OIDC features. The receiver owns a separate SQLite database for its Inbox and queryable processing receipts.

## Run and observe

From the repository root, with Docker running and the pinned .NET SDK installed:

```sh
dotnet run --project sample/NOF.Sample.AppHost
```

AppHost starts RabbitMQ and the receiving service before the producer. Open the producer's HTTP endpoint from the Aspire dashboard. Set `PRODUCER` and `RECEIVER` to the HTTP addresses shown for `NOF-Sample` and `sample-notifications` (their ports may vary).

```sh
curl -i -X POST "$PRODUCER/api/messages/orders" \
  -H 'Content-Type: application/json' -d '{"email":"demo@example.test"}'
# 202 Accepted
# {"orderId":"<generated-guid>"}

curl "$RECEIVER/api/messages/receipts?orderId=<generated-guid>"
```

Poll the second endpoint until its `items` contains three receipts: mail (kind 0), order audit (kind 1), and order metrics (kind 2). Without `orderId`, it returns the latest 100 receipts. Invalid email returns 400 and stages no messages. The demo accepts anonymous, host-tenant requests; it simulates an order submission and mail processing without persisting an order aggregate or sending real email.

Stop `sample-notifications` in the dashboard, submit another order, then restart it. After its durable subscriptions have been established at least once, RabbitMQ retains queued work while it is offline. Poll the receipts endpoint again to see processing resume. A completely new subscription cannot receive events published before its queue existed; the initial AppHost dependency prevents that race in this demo.

## Where routing lives

Application stages two transport-independent intents in the same `IUnitOfWork` and saves once:

```csharp
await outbox.EnqueueAsync(new OrderCreated(orderId), context, cancellationToken: ct);
await outbox.EnqueueAsync(new SendEmail(email, "Created", "Order created.", OrderId: orderId), context, cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

The producer explicitly selects scoped infrastructure adapters and stable logical schemas:

```csharp
builder.Services.AddOutbox(outbox =>
{
    outbox.Add<SendEmail, SendEmailOutboxHandler>("sample.send-email", 1);
    outbox.Add<OrderCreated, OrderCreatedOutboxHandler>("sample.order-created", 1);
});
```

`NOF.Sample.Messaging/MessageOutboxHandlers.cs` chooses the RabbitMQ client and physical destination. These calls run in separate adapters:

```csharp
await client.SendAsync(RabbitMQDemoRoutes.MailQueue, message, deliveryContext, ct);
await client.PublishAsync(RabbitMQDemoRoutes.OrderCreatedTopic, message, deliveryContext, ct);
```

Forward the entire `OutboxDeliveryContext`: its overload preserves the original message ID, trace and ordering metadata across retries. Passing only its semantic `Context` would create a new delivery identity. Logical schema IDs are not queue names; changing a physical destination does not require changing Application's enqueue calls.

The receiving host's `NotificationMessaging.cs` explicitly binds infrastructure routes to operations:

```csharp
builder.Services.AddRabbitMQ(options => options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq"), endpoints =>
{
    endpoints.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue);
    endpoints.MapSubscription<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderCreatedTopic, RabbitMQDemoRoutes.AuditSubscription);
    endpoints.MapSubscription<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderCreatedTopic, RabbitMQDemoRoutes.MetricsSubscription);
});
```

| Intent | Physical destination | Consumer operation |
|---|---|---|
| `SendEmail` | queue `sample.mail.v1` | `SendMail` |
| `OrderCreated` | topic `sample.orders.created.v1`, subscription `sample-order-audit` | `RecordOrder` |
| `OrderCreated` | same topic, subscription `sample-order-metrics` | `CountOrder` |

A queue distributes commands among competing consumers; each independently named topic subscription receives its own event copy. Scaling a subscription with the same name adds competing workers, not another event recipient. The adapter declares direct/fanout exchanges and durable queues. Routing constants, bindings, broker configuration and clients stay in infrastructure; Contract declares payloads/operations and Application contains handlers plus a receipt port.

Both hosts call `AddRabbitMQ` with the `rabbitmq` connection string injected by Aspire. The receiver enables Inbox by configuring RabbitMQ endpoints. The producer has no consumers; it retains an inactive Inbox capability (`AddInbox`, `Inbox.RunWorkers=false`) solely to preserve its existing PostgreSQL migration model alongside Outbox. Receiver handlers stage projections through `INotificationReceipts`; they do not save independently. The Inbox worker commits those changes with its processed receipt in the same SQLite unit of work. Acknowledgement at the broker means durable Inbox acceptance, not completion of the business handler.

## Persistence and verification

The receiver uses `notifications.db` in its project working directory. NOF initializes a fresh SQLite schema on first use; this disposable demo has no migration chain. Stop the receiver before deleting its database to reset the demo. Production deployments need an explicit migration strategy. The existing producer PostgreSQL migrations and their upgrade precautions still apply; this sample adds no migration or automatic cleanup of existing data. `SendEmail.OrderId` is optional, preserving the earlier v1 payload shape for pending messages.

Delivery is at least once. NOF keeps the message identity and uses Inbox deduplication per route; the two event subscriptions deliberately have separate Inbox routes for the same message ID. External effects such as real SMTP delivery would still need their own idempotency design.

The normal sample tests skip the live-broker test unless `NOF_SAMPLE_RABBITMQ` is configured. Use an isolated broker/vhost: the test declares the sample's fixed durable route names. In a shell with that environment variable set to a connection string:

```sh
dotnet test sample-tests/NOF.Sample.Tests -c Release --filter FullyQualifiedName~RabbitMQDispatchTests
```

The test starts independent producer/receiver hosts and databases, exercises real RabbitMQ queue delivery and fanout, stops/restarts the receiver, and compares Inbox IDs with Outbox IDs. An explicitly configured but unavailable broker fails the test. CI runs it in a dedicated job with a disposable RabbitMQ service. PostgreSQL upgrade validation remains a separate deployment concern.

## Adapter boundary and receipt-key migration

MessageBinding, MessageBindingRegistry and AddMessageBindings are removed. The RabbitMQ endpoint registry belongs to the RabbitMQ package; Memory has its own independent endpoint registry. The common Inbox has no queue/topic/subscription lookup. It resolves a scoped `IInboxHandler` through native keyed DI using the receipt's opaque Route, then processes it in the existing tenant scope and transaction. `OperationInboxHandler` invokes the selected operation through the shared authorization/filter boundary. Custom adapters can register their own handler without any service operation or topology registry.

New Inbox keys include the transport: `rabbitmq:queue:<queue>`, `rabbitmq:subscription:<subscription>`, and corresponding `memory:` keys. Physical broker queue/exchange names and Outbox schema IDs remain unchanged. Before upgrading, stop and drain old consumers and Inbox work, back up Inbox and order-state data, and preserve deduplication history. Existing `queue:`/`subscription:` receipts and order-state routes require an explicit mapping to the chosen transport, updating both Inbox.Route and InboxOrderState.Route together while offline. Retain message IDs, ordering state and completed history; resolve conflicts and competing-consumer deployments before resuming. There is no automatic remapping, deletion or table migration in this change. Replaying previously processed messages without their mapped history can repeat business effects.
