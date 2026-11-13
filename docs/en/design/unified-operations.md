# Unified operations, message bindings and aggregate collections

[简体中文](../../zh-CN/design/unified-operations.md) · [Invocation extensions](invocation-extensions.md) · [Persistence boundaries](persistence-boundaries.md)

ApplicationService is the only application operation model for HTTP, local invocation and asynchronous consumers. CommandHandler, NotificationHandler, their invokers, registration generators and registries are removed. In-memory domain events retain their current-scope event mechanism.

## Contract and implementation

```csharp
// Contract: no partial modifier is needed for plain operations.
public interface IMailService : IApplicationService
{
    Empty Send(SendMail request);
}

// Application: implementations still belong to ApplicationService.
public partial class MailService : ApplicationService<IMailService>;
public sealed class SendMailHandler(IMailGateway gateway) : MailService.Send
{
    public override async Task<Empty> HandleAsync(
        SendMail request, InvocationContext invocation, CancellationToken ct)
    {
        await gateway.SendAsync(request, ct);
        return Empty.Instance;
    }
}
```

Contract generates the input-only IMailServiceOperations.Send reference. Application generates MailService.Send and a complete typed Descriptor; its Reference has the same stable operation identity. Producers reference only Contract. All contract interfaces, including FailureCase declarations, and IApplicationServiceImplementation need no partial. Result and failure branches are generated directly in Application operations. Operation identity is contract plus method, never the input/output pair or handler implementation name.

Register the application assembly and each service container once. Individual operations continue to be discovered automatically:

```csharp
builder.AddApplicationPart(typeof(MailService).Assembly);
builder.AddApplicationService<MailService>();
builder.Services.AddMessageBindings(bindings =>
{
    bindings.Queue<SendMail>("mail.send.v1").To(MailService.Send.Descriptor);
    bindings.Subscription<OrderCreated>("orders.created.v1", "orders-audit")
        .To(OrderAudit.Record.Descriptor);
    bindings.Subscription<OrderCreated>("orders.created.v1", "orders-projection")
        .To(OrderProjection.Apply.Descriptor);
});
```

Queue/Subscription.To accepts only `ServiceOperationDescriptor<T, Empty>`. An operation returning a DTO or FailureCase union cannot silently discard that output through an asynchronous binding. Multiple operations can consume the same payload. Each subscription gets its own receipt, deduplication key, ordered stream progress and retries. Instances using the same subscription ID compete; distinct subscription IDs receive independent copies. Choose globally namespaced, stable subscription IDs; deployment/service names no longer implicitly prefix them.

## Invocation versus submission

```csharp
// Wait for business execution, in an isolated local scope.
await MailService.Send.InvokeAsync(services, mail, context, ct);

// IMessageDispatcher: wait for transport acceptance, not business execution.
await messages.SendAsync(IMailServiceOperations.Send, mail, context, ct);
await messages.PublishAsync(new OrderCreated(orderId), context, ct);

// Stage in the current transaction; save business changes and delivery intent together.
await messages.DeferSendAsync(IMailServiceOperations.Send, mail, context, ct);
await messages.DeferPublishAsync(new OrderCreated(orderId), context, ct);
await unitOfWork.SaveChangesAsync(ct);
```

Ordered variants take the operation (for sending), payload, order key, Context, optional completesOrderKey and cancellation token. Their existing contiguous-sequence requirements remain. Acceptance is not exactly-once execution of external side effects.

A producer with no local consumer configures outbound routes only:

```csharp
services.AddMessageBindings(bindings =>
{
    bindings.RouteToQueue(IMailServiceOperations.Send, "mail.send.v1");
    bindings.Topic<OrderCreated>("orders.created.v1");
});
```

This installs no local consumer. Producer and receiver must agree on the stable route and payload schema. No CLR payload names are persisted as publication routes. Routing registries belong to DI, freeze on first use and reject conflicting identities.

## One execution boundary

`IOperationExecutor` executes a selected descriptor in the existing host scope and waits for completion. Local `IOperationInvoker` creates an isolated scope and copies verified identity; HTTP uses the request scope; Inbox uses its existing transaction scope so handler changes, receipt progress and newly staged messages commit together. No handler instance is constructed before invocation filters run.

Application-owned `InvocationContext` composes semantic `Context` and supplies Operation, InvocationId, Origin (Local/Http/Message), implementation policy metadata and ServiceExecution. Filters may replace semantic context through WithContext/WithTenantId; views share the execution outcome. `Context.Current` contains the semantic Context, not the InvocationContext. Pass `invocation.Context` to nested semantic APIs. Identity has one source, scoped IUserContext.

All origins run contract and implementation authorization policies. Message authentication does not deserialize producer claims or access tokens. Hosts establish their consumer identity through their selected pipeline/filter before authorization; an anonymous consumer cannot invoke a protected operation. Broker ingress and tenant headers must be restricted by the host's trust boundary. Strict tenant options apply to all operation origins.

HTTP status and challenge headers stay in HttpRequestInboundAdapter. RejectionReason identifies Policy/Authentication/Authorization without an HTTP status code in the execution context. Policy rejection is distinct from a completed business failure union. Technical exceptions propagate. Inbox records rejected operations as terminal failures; technical failures follow its retry policy. Transport acknowledgement follows durable receipt acceptance, not handler completion.

The memory and RabbitMQ transports use the same MessageInboxReceiver. It establishes the delivery tenant before resolving scoped persistence, and treats a database conflict as duplicate acceptance only after confirming the exact message ID/consumer receipt exists.

## Breaking migration and stored messages

TenantAuthorizationOptions.RequireAuthenticatedTenantForRequests and RejectMismatchedTenantForRequests are renamed to RequireAuthenticatedTenant and RejectMismatchedTenant; their policies now apply to every operation origin.

Replace ICommandSender/INotificationPublisher with IMessageDispatcher. Replace message handlers with Empty-returning service operations, declare explicit queue/subscription bindings, and pass an operation descriptor to Send/DeferSend. Replace the three inbound context subclasses and InvocationKind with InvocationContext plus Origin. Use IOperationExecutor for an existing scope and IOperationInvoker for isolated local calls. Removed APIs have no compatibility adapter.

Do not start the new consumers against old routing records without preparation. The safest upgrade is to stop producers, drain the old outbox, broker queues and inbox with the old deployment, verify no pending/claimed/failed records need recovery, then deploy both sides with the new bindings and resume production. This requires no schema migration.

If draining is impossible, stop all workers and create an application-owned migration with an explicit old-to-new mapping: old outbox DispatchRoutes payload-type strings to stable queue/topic routes; old inbox Route handler/queue names to `queue:<queue>` or `subscription:<subscription>`; and the corresponding NOFInboxOrderState.Route keys in every tenant database. Include retained processed receipts to preserve deduplication, resolve key collisions, preserve ordering/sequence progress and migrate/replay broker backlog. Back up and validate the mapping before restarting. NOF never guesses a handler-name migration or silently drops an unresolved receipt.

## Aggregate-owned collections

```csharp
public partial class ConfigNode
{
    [ReadOnlyCollection]
    private readonly List<ConfigFile> _configFiles = [];

    [ReadOnlyCollection("Tags")]
    private readonly HashSet<ConfigTag> _tags = new(ReferenceEqualityComparer.Instance);
}

entity.OwnsMany(node => node.ConfigFiles);
entity.OwnsMany(node => node.Tags);
```

Domain generates cached live IReadOnlyCollection views and CollectionBackingField metadata. The wrapper never copies elements or exposes ICollection/List/HashSet mutation interfaces. Aggregate methods mutate the private fields. It protects collection membership, not the mutability of each element. Use reference equality for entity HashSet navigations.

The first version accepts private readonly List<T>/HashSet<T> fields in top-level, non-generic partial classes. `_configFiles` becomes ConfigFiles; an optional attribute argument overrides the property name. NOF020 reports unsupported fields or conflicting/invalid names; NOF021 requests partial, with a code fix. Initialize fields before accessing their views.

The EF Core adapter applies field access only to relationships explicitly mapped as OwnsMany, after application model configuration. It validates the association and rejects conflicting explicit access modes/field mappings. The attribute creates no relationship. HasMany, skip navigations and NHibernate collection replacement are outside this automatic convention. The sample ConfigNode uses the generated view and existing strong-typed OwnsMany mapping. The unsafe general Mut extension is removed; use aggregate methods instead.

[Application-owned failure results and descriptor migration](expected-failures.md).

[Current Contract/Application/Abstraction boundaries](contract-boundaries.md).
