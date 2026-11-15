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

The current persistence tenant is authoritative; explicit mismatches are rejected. Enqueue snapshots every JSON-scalar Context item, payload, explicit Headers, normalized tenant, stable message ID and enqueue trace. No IUserContext/claims or DI/runtime state is serialized. Each attempt binds the restored Context before constructing the handler in a fresh DI scope.

Pass optional `IReadOnlyDictionary<string, string?> headers` as the third argument; enqueue snapshots the dictionary immediately, including null. OutboxDeliveryContext.Headers is read-only and ready to forward, including X-Message-Id, X-Tenant-Id, traceparent and the JSON snapshot in X-NOF-Context. These case-insensitive managed keys cannot be overridden. Native adapters forward this metadata directly; MessageDelivery is removed.

```csharp
var messageId = await outbox.EnqueueAsync(message, invocation.Context,
    headers: new Dictionary<string, string?> { ["correlation-id"] = correlationId },
    cancellationToken: ct);
```

NOF provides no send/receive ordering guarantee. OutboxEnqueueOptions, sequence allocation, ordering headers/constants, stream leases, gap waiting, completion flags and failure blocking are removed. Old ordering-looking headers are opaque values. CreatedAtUtc/Id sorting only schedules batches; concurrent workers, retries and multiple queues may reorder delivery. A permanently failed record does not block other work. Business versions belong in message data. Infrastructure owns ordering when required, including its persistence and claim scheduling, producer/queue concurrency, and consumer recovery; adding a header or serializing one publisher alone does not guarantee end-to-end order.

## Infrastructure registration and handlers

```csharp
builder.AddApplicationPart(typeof(SendOrderConfirmationHandler).Assembly);
builder.Services.AddTransactionalMessages();

public sealed class SendOrderConfirmationHandler(MailClient client)
    : OutboxHandler<SendOrderConfirmation>
{

    public override async ValueTask HandleAsync(
        SendOrderConfirmation message, OutboxDeliveryContext context, CancellationToken ct)
    {
        // MailClient is the application's concrete client, with its own acceptance contract.
        await client.SendConfirmationAsync(message.Email, message.OrderId,
            idempotencyKey: context.MessageId.ToString(), cancellationToken: ct);
    }
}
```

Concrete OutboxHandler<TMessage> classes handle exactly one message type. The Infrastructure generator discovers accessible, non-generic handlers and registers scoped implementations and typed delivery delegates through AddApplicationPart. Implement only HandleAsync: no Attribute, partial modifier, MessageType or SchemaVersion declaration is needed. The persisted identity is inferred from typeof(TMessage).ToString(), including namespace and closed generic arguments but excluding assembly versions; identities must be unique in a host and fit the existing 512-character column. NOF330 rejects inaccessible handlers or obsolete metadata declarations; NOF331 rejects duplicate payload handlers, with runtime checks across assemblies. Registration never constructs handlers or activates workers. AddTransactionalMessages enables the capability explicitly. Open generic templates can be manually closed with AddOutboxHandler<TMessage,THandler>(), without metadata arguments.

The inferred identity is looked up only in the registered typed delegate catalog. It is never passed to Type.GetType or used to dynamically load persisted CLR names. SchemaVersion is removed from the entity, stores, registry and dispatch APIs. A handler/type rename does not need registration edits, but renaming the payload type/namespace changes its durable key and requires draining or explicitly migrating existing records. Incompatible payload evolution belongs to application-owned types and migration, not framework version dispatch.

Migration: replace IOutboxHandler<T> with OutboxHandler<T>, split multi-message adapters and remove MessageType/SchemaVersion declarations and registration arguments. Keep AddApplicationPart plus AddTransactionalMessages. Stop old binaries, back up every tenant database and explicitly convert old keys to reviewed inferred identities. The sample's SimplifyOutboxMetadata migration maps its two known unchanged v1 payload IDs, refuses non-v1 pending/failed work, and drops SchemaVersion while preserving IDs, payloads, headers, delivery state and Inbox history. Other applications supply their own reviewed key/payload mapping; no runtime alias layer guesses old formats. Downgrade requires the matching backup because dropped version values cannot be reconstructed.

OutboxDeliveryContext exposes MessageId, Context, Attempt and Headers. It is delivery metadata, not InvocationContext; an intent need not execute a NOF operation. The normal return from a handler must mean that the configured client has met its acceptance contract. Retryable exceptions follow MaxRetryCount and optional RetryDelay; OutboxPermanentFailureException terminates an unrecoverable delivery immediately. Unknown message identities and malformed registered JSON also fail permanently, rather than dynamically loading CLR types or guessing an adapter.

Each CLR message type and inferred identity has exactly one handler per host. Registrations are DI-local and freeze on first use. Represent independently retryable channels with distinct intent types. Drain or explicitly migrate stored records before deploying incompatible payload changes. Register the payload's generated JsonSerializerContext in host-owned JSON options through AddNOFJson for Native AOT; registry delegates remain statically typed and use IObjectSerializer without runtime CLR-name lookup.

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

MemoryMessageClient is registered by AddMemoryMessaging; AddRabbitMQ provides native connection access and optional queue consumers. Only ConfigureRabbitMQ with queue mappings enables inbound broker consumers. Adapter-owned queue/subscription endpoints still pass through the common execution policies and accept only void operations without business FailureCase. One operation can now receive several explicit queues because there is no outbound operation-to-queue lookup. RouteToQueue, Topic and GetQueue/GetTopic APIs are removed.

The sample's SubmitDemoOrder handler directly stages SendEmail and OrderCreated, then saves the same unit of work once. NOF.Sample host adapters choose native RabbitMQ destinations; NOF.Sample.Notifications maps the receiving queues to operations. See the [two-service routing sample](rabbitmq-sample.md). Changing delivery affects infrastructure without changing Application's enqueue API.

## Durable processing and migration

Records use inferred string MessageType, Payload, explicitly staged Headers, TraceParent, status/attempt/claim fields, NextAttemptAtUtc. DispatchRoutes and OutboxMessageType are removed. Claims exclude delayed attempts and live leases. Completion, failure and release are fenced by the claim owner; a stale worker cannot overwrite a newer claimant. Shutdown leaves an uncertain started attempt for lease recovery and releases unstarted work without charging an attempt.

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

AddNOF alone does not register IOutbox, durable Inbox/Outbox stores, their model contributors or their processing/cleanup workers. AddTransactionalMessages assembles Inbox and Outbox together: both stores/registries, staging and receipt APIs, shared persistence, and all four processing/cleanup workers. Both directions run by default; configure Inbox.RunWorkers and Outbox.RunWorkers independently. AddMemoryMessaging and ConfigureRabbitMQ with queue mappings call AddTransactionalMessages; client-only AddRabbitMQ installs no consumers or Inbox workers. Calls are idempotent at the infrastructure level, respect custom store/provider registrations, and work before or after AddNOF. Duplicate message/schema declarations remain errors. Default scheduling options validate only for enabled capabilities whose workers run.

```csharp
builder.AddApplicationPart(typeof(DeliverOrderCreated).Assembly);
builder.Services.AddTransactionalMessages();
builder.Services.Configure<TransactionalMessageOptions>(options => options.Outbox.RunWorkers = false);
```

A producer-only host still stages snapshots and commits its unit of work, but does not claim, deliver, retry or clean up messages. Inbox.RunWorkers=false similarly accepts durable receipts without processing or cleanup. Independent worker hosts use the same AddTransactionalMessages entry and schemas/bindings, enable the required direction with RunWorkers, and select appropriate persistence/transport configuration. Disabling workers never discards pending records or resets ownership state. Replace AddInbox/AddOutbox with one AddTransactionalMessages call; there are no compatibility aliases. Hosts that already map both tables need no schema change. Previously single-direction EF/NHibernate models now include both tables: review the additive migration before deployment. Disabling workers retains both models and all durable state; existing generic-outbox migrations still apply when upgrading old schemas.

Custom IOutboxStore implementations remain usable without an ORM or transport under DI dependency validation. AddNOF registers core execution; hosts explicitly select optional capabilities and concrete providers.

Capability configuration does not edit existing model snapshots or execute table drops. Migration-owning hosts sharing a database must keep its full capability model; do not generate destructive removals merely to disable a process worker. RunWorkers=false retains the model and all durable progress.

Transport adapters now own their endpoint mappings. See the [adapter boundary and receipt-key migration](rabbitmq-sample.md#adapter-boundary-and-migration) before changing existing Inbox receipt routes.

Inbox receipts now use only ID and opaque Route; the unused Command/Notification label and Memory Rider wrappers are removed. Apply the explicit classification-column migration and select native persistence as described in [persistence boundaries](persistence-boundaries.md). The default memory backend stores message state only.

See [JSON context and automatic durable delivery](execution-context.md) for the breaking Context API, ready-to-send headers and fluent registration.
