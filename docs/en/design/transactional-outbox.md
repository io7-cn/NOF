# Transactional Outbox and direct message endpoints

[简体中文](../../zh-CN/design/transactional-outbox.md) · [RabbitMQ sample](rabbitmq-sample.md) · [One-way operations](one-way-operations.md)

Application code stages transport-independent intents through IOutbox. Infrastructure OutboxHandler<T> implementations choose a native client and destination. A consumer endpoint directly executes its OperationHandler; NOF provides no Inbox, receipt table, consumer deduplication or consumer retry scheduler. Business code owns idempotency.

## Application staging

```csharp
var id = await outbox.EnqueueAsync(message, invocation.Context,
    headers: new Dictionary<string, string?> { ["correlation-id"] = correlationId },
    cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

EnqueueAsync returns ValueTask<Guid> identifying a staged snapshot, not a completed effect. It serializes the payload and snapshots Context and optional IReadOnlyDictionary<string,string?> Headers immediately, without sending or saving. Save the same scoped unit of work as the business changes; an explicit transaction also requires CommitAsync. Disposal or rollback discards uncommitted intents. Native EF/NHibernate stores share the application's scoped session and transaction. The development memory backend persists Outbox state only and is not process-durable business storage.

The current persistence tenant is authoritative; explicit mismatches are rejected. Every JSON-scalar Context item, explicit header, normalized tenant, stable message ID and original trace is saved. Claims, IUserContext and runtime objects are never serialized. Managed message ID/tenant/trace/X-NOF-Context keys are reserved. Retries restore the saved Context before constructing a scoped handler and preserve read-only, ready-to-send OutboxDeliveryContext.Headers. Forward those headers directly; MessageDelivery and OutboxEnqueueOptions are removed.

## Infrastructure registration and handlers

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

AddApplicationPart automatically registers accessible nongeneric concrete handlers and typed delivery delegates without constructing them or enabling workers. Each handler implements only HandleAsync for one payload type. No Attribute, partial modifier, MessageType or SchemaVersion declaration is needed. Inferred durable identity is typeof(T).ToString(), including namespace/closed generic arguments and excluding assembly versions; it must be unique and fit the 512-character column. NOF330 validates access and removed metadata; NOF331 rejects duplicate payload handlers. Explicit AddOutboxHandler<TMessage,THandler>() supports manually selected closed generic adapters. Persisted identities resolve only through registered delegates, never Type.GetType or dynamic loading.

OutboxDeliveryContext supplies MessageId, Context, Attempt and Headers. Handler completion means the concrete client's delivery contract completed, not that a remote business effect happened exactly once. External effects need a stable business/provider idempotency key. Returning normally permits marking the intent Sent; thrown technical errors use Outbox retry policy. Unknown schemas and explicitly permanent delivery failures become failed records without blocking unrelated work. Claims, completion, failure and release remain owner-fenced; expired exhausted claims are recovered.

## Capability assembly and worker hosts

AddNOF, AddMemoryMessaging and RabbitMQ MapQueue install no Outbox store, model or worker. AddOutbox explicitly installs staging, default message-only memory persistence when no native/custom store was selected, and the two Outbox processing/cleanup workers. Calls are idempotent, preserve custom stores, and work before or after AddNOF. Handler/schema conflicts remain errors. Configure the flat OutboxOptions directly; TransactionalMessageOptions and its nested Inbox/Outbox settings are removed.

OutboxOptions.RunWorkers=false retains staging and storage while stopping processing and cleanup in a producer-only host. A separate worker host uses the same schemas and persistence. Scheduling options are validated when workers run. Disabling workers retains models and pending state; it does not drop tables or reset claims.

## Consumer execution and idempotency

RabbitMQ invokes the mapped operation in a fresh DI scope with restored Context and the normal authorization/filter pipeline. It ACKs only after the operation and application-owned commit boundary finish. Failures and filter rejections are not successful deliveries: native adapter settings decide NACK/requeue/dead-letter behavior. Native MessageId overrides the legacy header, supports non-Guid IDs, and is preserved without generating a replacement for each redelivery. Missing transport identity stays missing; business code may use a business key instead. No producer claims are restored.

The adapter creates no mandatory database transaction and never implicitly saves business changes. Handlers or application filters own saves/commits. A commit followed by a lost ACK can execute again. Enforce a unique business key/version atomically with the effect; a separate existence check is insufficient under concurrency. New messages staged during consumption must be saved in the same business transaction. External effects belong behind Outbox/provider idempotency.

Memory SendAsync waits for the local handler to finish and propagates failures/cancellation. PublishAsync invokes each selected local subscription in its own scope. Partial fanout may be replayed by an upstream Outbox; successful subscriptions also need business idempotency. Direct Memory calls have no process-restart delivery guarantee. Contract void still has no business reply; waiting for transport execution does not add Result or FailureCase responses.

NOF guarantees no send/receive ordering. Infrastructure owns native concurrency, scheduling, retries and order requirements. Ordering-looking custom headers remain opaque. CreatedAtUtc/Id ordering only schedules Outbox batches. Native RabbitMQ topology, prefetch, retry and dead-letter policies belong in infrastructure; avoid unbounded immediate requeue loops.

## Breaking migration and existing databases

Replace AddTransactionalMessages with AddOutbox only in producers/Outbox workers. Remove Inbox options, custom IInboxStore implementations, receipt registries and worker integrations. Configure OutboxOptions directly, for example options.PollingInterval rather than options.Outbox.PollingInterval. Consumers need no Outbox registration unless they stage new intents. Port historical Inbox-based idempotency to business constraints before enabling consumers.

Stop old consumers/workers and back up every tenant database. Old Inbox messages may already have been ACKed by the broker: resolve them with the old application, or explicitly export/replay reviewed payloads and metadata. Preserve/export completed receipt history when it is still needed. Never run old and new consumers together during cutover. The sample's 20261116000000_RemoveInbox migration locks the retired table, refuses any Pending/Failed row, and then drops only NOFInboxMessage. It neither changes Outbox data nor guesses replay destinations. Downgrade requires matching backups and old binaries. No live migration is executed by this change.

Historical GenericOutbox, RemoveMessageOrdering, RemoveInboxClassification and SimplifyOutboxMetadata migrations remain intact and must be reviewed in order for old databases. SimplifyOutboxMetadata maps only the sample's two unchanged v1 payloads, refuses unsupported pending versions and drops SchemaVersion. Payload namespace/type changes still need explicit key/payload migration. No aliases or automatic conversion of unknown messages are provided.

See [JSON contexts](execution-context.md) and the [RabbitMQ sample](rabbitmq-sample.md) for complete context propagation and application-owned projection idempotency.
