# Generic transactional outbox

[简体中文](../../zh-CN/design/transactional-outbox.md) · [Unified operations](unified-operations.md) · [Persistence boundaries](persistence-boundaries.md)

Application stages durable intents through IOutbox. Immediate calls use a concrete HTTP client, SDK, MemoryMessageClient or RabbitMQMessageClient. Infrastructure decides what each intent means and when delivery has succeeded. There is no IMessageDispatcher, outbound operation routing, ICommandRider/INotificationRider interface, or Command/Notification branch in the outbox engine.

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

The current persistence tenant is authoritative. An explicit mismatching tenant is rejected. Only the normalized tenant and producer trace are persisted; access tokens, claims and arbitrary Context.Items are not replayed. Delivery restores a fresh Context and a fresh DI scope for each attempt, without impersonating the original request user.

For a stream, pass new OutboxEnqueueOptions { OrderKey = "order:42", CompletesOrderKey = false }. The final intent can set CompletesOrderKey to true. Keys are qualified by service and tenant persistence, with the existing contiguous sequence allocation. Later records cannot overtake an unfinished or permanently failed predecessor. Independent streams continue processing. Recover a failed stream explicitly; the framework never silently skips its failed intent.

## Infrastructure registration and handlers

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
        // MailClient is the application's concrete client, with its own acceptance contract.
        await client.SendConfirmationAsync(message.Email, message.OrderId,
            idempotencyKey: context.MessageId.ToString(), cancellationToken: ct);
    }
}
```

OutboxDeliveryContext exposes MessageId, Context, Attempt, OrderKey, Sequence and CompletesOrderKey. It is delivery metadata, not InvocationContext; an intent need not execute a NOF operation. The normal return from a handler must mean that the configured client has met its acceptance contract. Retryable exceptions follow MaxRetryCount and optional RetryDelay; OutboxPermanentFailureException terminates an unrecoverable delivery immediately. Unknown schema IDs/versions and malformed registered JSON also fail permanently, rather than dynamically loading CLR types or guessing an adapter.

A CLR message type and a stable (MessageType, SchemaVersion) pair each have exactly one handler per host. Registrations are DI-local and freeze on first use. Represent independently retryable channels with distinct intent types. Preserve old schema registrations or explicitly migrate their records before deploying changed schemas. Register the payload's generated JsonSerializerContext in JsonSerializerOptions.NOF for Native AOT; registry delegates remain statically typed and use IObjectSerializer without runtime CLR-name lookup.

## Concrete transport clients and inbound bindings

```csharp
// Immediate transport acceptance; the caller names the actual queue/topic.
await memoryClient.SendAsync("mail.send.v1", message, context, ct);
await rabbitClient.PublishAsync("orders.created.v1", orderCreated, context, ct);

// An outbox adapter forwards stable ID and ordering metadata with this overload.
await memoryClient.SendAsync("mail.send.v1", message, deliveryContext, ct);

// Inbound only: physical topology is independent of the outbox schema.
services.AddMessageBindings(bindings =>
    bindings.Queue<SendEmail>("mail.send.v1").To(MailService.Send.Descriptor));
```

MemoryMessageClient is registered by AddNOF; AddRabbitMQ additionally registers RabbitMQMessageClient without silently replacing the memory client. AddRabbitMQ still configures inbound broker consumers. Queue/Subscription bindings still pass through the common execution policies and reject non-Empty results or declared failures. One operation can now receive several explicit queues because there is no outbound operation-to-queue lookup. RouteToQueue, Topic and GetQueue/GetTopic APIs are removed.

The sample's TransactionalMessages stages SendEmail and OrderCreated. Host SendEmailOutboxHandler and OrderCreatedOutboxHandler choose the memory client and physical routes; the existing mail and order operations consume the inbound messages. Switch infrastructure handlers to a broker, HTTP client or SDK without changing Application's enqueue API.

## Durable processing and migration

Records use stable string MessageType plus SchemaVersion, Payload, allowlisted Headers, TraceParent, status/attempt/claim fields, NextAttemptAtUtc and optional stream fields. DispatchRoutes and OutboxMessageType are removed. Claims exclude delayed attempts and blocked stream successors. Completion, failure and release are fenced by the claim owner; a stale worker cannot overwrite a newer claimant. Shutdown leaves an uncertain started attempt for lease recovery and releases unstarted work without charging an attempt.

Delivery is at least once. A process can exit after an external side effect and before completion is persisted. Every retry keeps the original MessageId; concrete clients and external systems must honor their chosen idempotency semantics. Multiple external systems in one handler can partially succeed; separate intents when independent recovery is required. Handler-local changes are not automatically committed by the outbox worker.

Breaking upgrade procedure:

1. Stop producers on the old package set, drain its pending outbox and explicitly resolve failed records. Do not let old workers run against the new schema.
2. Back up the host and every tenant database, register all new intent schemas and configure their JSON metadata and handlers.
3. Apply the matching provider migration to each database. The PostgreSQL sample migration 20261113130000_GenericOutbox takes a table lock and rejects any legacy row that is not Sent before replacing legacy type/routes. Historical completed rows become legacy.completed, schema 1; they are never redelivered. No pending payload is automatically reinterpreted.
4. Deploy the coherent runtime/provider/host set and resume producers. Replace DeferSend/DeferPublish variants with EnqueueAsync and options; replace immediate dispatcher calls with concrete clients. NOFTestScope.EnqueueAsync stages in the existing scope; save that scope explicitly. Test-host immediate Send/Publish wrappers are removed.
5. Rollback restores the backup and matching previous packages together. Automatic Down is refused because generic intent schemas cannot be inferred as legacy routes. New schemas must not be opened by old binaries.

The migration is supplied as code; applying it to a live PostgreSQL database is a deployment step. SQLite regressions exercise the shared transactional store, stream blocking and owner fencing. Other tests cover snapshot/rollback, retry identity/scopes, independent subscriptions, tenant isolation, cancellation recovery and reflection-disabled typed delivery. Broker acknowledgement behavior remains the concrete client's responsibility; local tests do not run a live broker or PostgreSQL deployment.
