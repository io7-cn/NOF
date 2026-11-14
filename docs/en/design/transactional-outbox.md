# Generic transactional outbox

[简体中文](../../zh-CN/design/transactional-outbox.md) · [Unified operations](unified-operations.md) · [Persistence boundaries](persistence-boundaries.md)

Application stages durable intents through IOutbox. Immediate calls use a concrete HTTP client, SDK, MemoryMessageClient or native RabbitMQ IChannel. Infrastructure decides what each intent means and when delivery has succeeded. There is no IMessageDispatcher, outbound operation routing, ICommandRider/INotificationRider interface, or Command/Notification branch in the outbox engine.

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

IOutbox.EnqueueAsync returns ValueTask<Guid>. Its ID identifies a staged snapshot, not a completed side effect. Payload serialization happens at enqueue time; later mutation of the original object cannot change the intent. It performs no network call and does not save or commit. Save the same scoped unit of work; an explicit transaction also requires CommitAsync. Disposing or rolling back an unsaved scope discards its intents with its business changes.

The current persistence tenant is authoritative. An explicit mismatching tenant is rejected. The normalized tenant and producer trace accompany explicitly supplied headers; the current identity, access tokens, claims and arbitrary Context.Items are not copied. Headers are durable data: callers must deliberately choose safe values rather than copying request headers wholesale. Delivery restores a fresh Context and a fresh DI scope for each attempt, without impersonating the original request user.

Pass optional `IReadOnlyDictionary<string, string?> headers` as the third argument. Enqueue copies and serializes the dictionary immediately, including null values. `OutboxDeliveryContext.Headers` exposes a read-only snapshot on every attempt. Header keys are case-insensitive; attempts to supply NOF-managed `X-Message-Id`, `X-Tenant-Id` or `traceparent` fail before staging. MessageDelivery copies custom headers and supplies the stable message ID, authoritative tenant and current attempt trace.

```csharp
var messageId = await outbox.EnqueueAsync(message, invocation.Context,
    headers: new Dictionary<string, string?> { ["correlation-id"] = correlationId },
    cancellationToken: ct);
```

NOF provides no send/receive ordering guarantee. OutboxEnqueueOptions, sequence allocation, ordering headers/constants, stream leases, gap waiting, completion flags and failure blocking are removed. Old ordering-looking headers are opaque values. CreatedAtUtc/Id sorting only schedules batches; concurrent workers, retries and multiple queues may reorder delivery. A permanently failed record does not block other work. Business versions belong in message data. Infrastructure owns ordering when required, including its persistence and claim scheduling, producer/queue concurrency, and consumer recovery; adding a header or serializing one publisher alone does not guarantee end-to-end order.

## Infrastructure registration and handlers

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
        // MailClient is the application's concrete client, with its own acceptance contract.
        await client.SendConfirmationAsync(message.Email, message.OrderId,
            idempotencyKey: context.MessageId.ToString(), cancellationToken: ct);
    }
}
```

Concrete OutboxHandler<TMessage> classes handle exactly one message type. The Infrastructure source generator discovers accessible, non-generic handlers and registers scoped implementations and typed delivery delegates through AddApplicationPart; no Attribute or partial modifier is needed. Each handler declares a public const string MessageType; an optional positive public const int SchemaVersion defaults to 1. These constants describe the durable schema, not a queue or CLR name. Invalid declarations report NOF330; duplicate message types or schemas within an assembly report NOF331, while cross-assembly conflicts fail during host composition. Registration never constructs handlers or enables workers. AddOutbox() explicitly enables the capability, before or after adding application parts. Open generic templates are not discovered; closed adapters may be registered explicitly through AddOutboxHandler<TMessage, THandler>(id, version) when the host needs manual selection.

Migration: replace IOutboxHandler<T> with OutboxHandler<T>, mark HandleAsync as override, split multi-message implementations, move the existing IDs/versions unchanged into constants, and replace the AddOutbox callback with AddApplicationPart plus AddOutbox(). OutboxBuilder is removed; persisted records require no format change.

OutboxDeliveryContext exposes MessageId, Context, Attempt and Headers. It is delivery metadata, not InvocationContext; an intent need not execute a NOF operation. The normal return from a handler must mean that the configured client has met its acceptance contract. Retryable exceptions follow MaxRetryCount and optional RetryDelay; OutboxPermanentFailureException terminates an unrecoverable delivery immediately. Unknown schema IDs/versions and malformed registered JSON also fail permanently, rather than dynamically loading CLR types or guessing an adapter.

A CLR message type and a stable (MessageType, SchemaVersion) pair each have exactly one handler per host. Registrations are DI-local and freeze on first use. Represent independently retryable channels with distinct intent types. Preserve old schema registrations or explicitly migrate their records before deploying changed schemas. Register the payload's generated JsonSerializerContext in host-owned JSON options through AddNOFJson for Native AOT; registry delegates remain statically typed and use IObjectSerializer without runtime CLR-name lookup.

## Concrete transport clients and inbound bindings

```csharp
// Immediate transport acceptance; the caller names the actual queue/topic.
await memoryClient.SendAsync("mail.send.v1", message, context, ct);
await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, payload, ct);

// An outbox adapter forwards stable ID and custom headers with this overload.
await memoryClient.SendAsync("mail.send.v1", message, deliveryContext, ct);

// Inbound only: physical topology is independent of the outbox schema.
services.AddMemoryMessaging(endpoints =>
    endpoints.MapQueue<MailService.Send>("mail.send.v1"));
```

MemoryMessageClient is registered by AddNOF; AddRabbitMQ provides native connection access and optional queue consumers. Only ConfigureRabbitMQ with queue mappings enables inbound broker consumers. Adapter-owned queue/subscription endpoints still pass through the common execution policies and reject non-Empty results or declared failures. One operation can now receive several explicit queues because there is no outbound operation-to-queue lookup. RouteToQueue, Topic and GetQueue/GetTopic APIs are removed.

The sample's SubmitDemoOrder handler directly stages SendEmail and OrderCreated, then saves the same unit of work once. NOF.Sample host adapters choose native RabbitMQ destinations; NOF.Sample.Notifications maps the receiving queues to operations. See the [two-service routing sample](rabbitmq-sample.md). Changing delivery affects infrastructure without changing Application's enqueue API.

## Durable processing and migration

Records use stable string MessageType plus SchemaVersion, Payload, explicitly staged Headers, TraceParent, status/attempt/claim fields, NextAttemptAtUtc. DispatchRoutes and OutboxMessageType are removed. Claims exclude delayed attempts and live leases. Completion, failure and release are fenced by the claim owner; a stale worker cannot overwrite a newer claimant. Shutdown leaves an uncertain started attempt for lease recovery and releases unstarted work without charging an attempt.

Delivery is at least once. A process can exit after an external side effect and before completion is persisted. Every retry keeps the original MessageId; concrete clients and external systems must honor their chosen idempotency semantics. Multiple external systems in one handler can partially succeed; separate intents when independent recovery is required. Handler-local changes are not automatically committed by the outbox worker.

Breaking upgrade procedure:

1. Stop producers on the old package set, drain its pending outbox and explicitly resolve failed records. Do not let old workers run against the new schema.
2. Back up the host and every tenant database, register all new intent schemas and configure their JSON metadata and handlers.
3. Apply the matching provider migration to each database. The PostgreSQL sample migration 20261113130000_GenericOutbox takes a table lock and rejects any legacy row that is not Sent before replacing legacy type/routes. Historical completed rows become legacy.completed, schema 1; they are never redelivered. No pending payload is automatically reinterpreted.
4. Deploy the coherent runtime/provider/host set and resume producers. Replace DeferSend/DeferPublish variants with EnqueueAsync and optional headers; replace immediate dispatcher calls with concrete clients. NOFTestScope.EnqueueAsync stages in the existing scope; save that scope explicitly. Test-host immediate Send/Publish wrappers are removed.
5. Rollback restores the backup and matching previous packages together. Automatic Down is refused because generic intent schemas cannot be inferred as legacy routes. New schemas must not be opened by old binaries.

For hosts already using generic intents, stop all producers and workers, drain or explicitly migrate outstanding ordered Outbox/Inbox work into the infrastructure implementation, resolve blocked sequences, and back up every tenant database. Apply `20261114090000_RemoveMessageOrdering` after `GenericOutbox`. It locks both receipt tables and both order-state tables and refuses unfinished ordered work or blocked inbox state. It drops the two order-state tables, their indexes and the three ordering columns on each message table. IDs, payloads, Headers, statuses, retry metadata and receipt history remain intact, including unordered pending records. Archive old order state if needed before applying it. Historical migrations are unchanged; no conversion from old sequence columns to headers is guessed. Rollback requires the matching backup and binaries; Down refuses to invent lost state. Custom stores remove ordering from ReceiveAsync/claims and now return only an integer count from CleanupAsync.

The migration is supplied as code; applying it to a live PostgreSQL database is a deployment step. SQLite regressions exercise the shared transactional store, independent claims and owner fencing. Other tests cover snapshot/rollback, retry identity/scopes, independent subscriptions, tenant isolation, cancellation recovery and reflection-disabled typed delivery. Broker acknowledgement behavior remains the concrete client's responsibility. The optional [RabbitMQ sample integration test](rabbitmq-sample.md) exercises live queue/fanout delivery and offline recovery; it does not validate a PostgreSQL deployment.

## Capability assembly and worker hosts

AddNOF alone does not register IOutbox, durable Inbox/Outbox stores, their model contributors or their processing/cleanup workers. AddOutbox registers the complete Outbox capability; AddInbox registers the complete transport-independent Inbox capability. AddMemoryMessaging and ConfigureRabbitMQ with queue mappings call AddInbox; client-only AddRabbitMQ installs no consumers or Inbox workers. Calls are idempotent at the infrastructure level, respect custom store/provider registrations, and work before or after AddNOF. Duplicate message/schema declarations remain errors. Default scheduling options validate only for enabled capabilities whose workers run.

```csharp
builder.AddApplicationPart(typeof(DeliverOrderCreated).Assembly);
builder.Services.AddOutbox();
builder.Services.Configure<TransactionalMessageOptions>(options => options.Outbox.RunWorkers = false);
```

A producer-only host still stages snapshots and commits its unit of work, but does not claim, deliver, retry or clean up messages. Inbox.RunWorkers=false similarly accepts durable receipts without processing or cleanup. Independent worker hosts enable the matching capability with the same schemas/bindings and appropriate persistence/transport configuration. Disabling workers never discards pending records or resets ownership state. No new schema migration is required by capability gating; existing generic-outbox migrations still apply when upgrading old schemas.

Custom IOutboxStore implementations remain usable without an ORM or transport under DI dependency validation. AddNOF registers core execution; hosts explicitly select optional capabilities and concrete providers.

Capability configuration does not edit existing model snapshots or execute table drops. Migration-owning hosts sharing a database must keep its full capability model; do not generate destructive removals merely to disable a process worker. RunWorkers=false retains the model and all durable progress.

Transport adapters now own their endpoint mappings. See the [adapter boundary and receipt-key migration](rabbitmq-sample.md#adapter-boundary-and-migration) before changing existing Inbox receipt routes.

Inbox receipts now use only ID and opaque Route; the unused Command/Notification label and Memory Rider wrappers are removed. Apply the explicit classification-column migration and select native persistence as described in [persistence boundaries](persistence-boundaries.md). The default memory backend stores message state only.
