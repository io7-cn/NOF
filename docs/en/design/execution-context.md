# JSON execution context and durable delivery

[简体中文](../../zh-CN/design/execution-context.md) · [Operations](unified-operations.md) · [Transactional Outbox](transactional-outbox.md)

Context is an immutable snapshot of a JSON object. Keys are case-insensitive strings; values are JSON strings, finite numbers, booleans or null. Arrays, objects, arbitrary CLR instances and object keys are excluded. There is no CLR type-name directory or dynamic type loading.

```csharp
var context = Context.Empty.WithTenantId("tenant-a")
    .WithItem("correlation", "order-42")
    .WithItem("priority", 3)
    .WithItem("audit", true)
    .WithItem("optional", null);

int priority = context["priority"]!.GetValue<int>();
JsonObject snapshot = context.Items;
```

WithItem/WithItems/WithoutItem return a new Context. Items and indexer/TryGetItem reads return detached nodes: modifying a snapshot cannot mutate a running invocation, an earlier context or Context.Empty. WithItems accepts a JsonObject, copies it and validates every value. HTTP context sources and test scopes use the same JSON scalar model. Move former object keys to application-owned string constants and serialize enum/date/identifier intent explicitly as JSON values. Use `JsonNode.GetValue<T>`/`TryGetValue` rather than casting a context value to a CLR object.

## Outbox owns the complete snapshot

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

Enqueue snapshots the payload, every semantic context item, explicit Headers, normalized persistence tenant, stable message ID and enqueue trace. The full JSON context is stored in the existing Headers document under the reserved X-NOF-Context key. No database column or payload wrapper is added. X-Message-Id, X-Tenant-Id, traceparent and X-NOF-Context are framework-owned header keys.

Each attempt restores the persisted Context and binds it before constructing the scoped Outbox handler. OutboxDeliveryContext contains Context, MessageId, Attempt and read-only ready-to-send Headers. Native adapters forward Headers directly, including context and trace, and copy the message ID to the transport's native ID field. They do not inject or call MessageDelivery; that service and its registrations are removed. Attempts retain the staged context/ID rather than borrowing values from whichever request or ambient scope is currently running.

Message endpoints execute operations directly in an isolated scope with restored Context and the normal policy/filter pipeline. NOF owns no Inbox storage, deduplication or consumer retry scheduler. Business code owns idempotency and transaction/save boundaries; RabbitMQ ACK follows operation completion. Native adapters own retries and dead-letter policy. Memory calls await execution and propagate failures. AddOutbox alone enables durable outgoing intents and workers; queue mapping enables no persistence. See [direct endpoints and migration](transactional-outbox.md).

Older pending records and native messages without X-NOF-Context can restore only their existing tenant/header data. They cannot recover items that older versions never saved. No schema migration is needed for this addition; retain message IDs, payloads, Headers and receipt history. Update packages and adapt the breaking Context APIs together.

## Fluent host setup

```csharp
services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "app:{tenantId}:")
    .AddMemoryCacheService();

services.AddRabbitMQ(factory => factory.Uri = connectionUri)
    .ConfigureRabbitMQ(OrdersMessaging.Configure)
    .ConfigureRabbitMQ(AuditMessaging.Configure);
```

ConfigureCacheServiceOptions replaces the abbreviated CacheServiceSelector.Configure name. It works before/after provider selection and composes in native options-registration order. AddRabbitMQ and ConfigureRabbitMQ already return IServiceCollection: native chaining needs no additional Selector or builder layer. Repeated configuration still uses one RabbitMQ lifecycle and preserves topology-before-consumer startup.
