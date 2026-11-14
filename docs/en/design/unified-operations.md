# Unified operations, transport endpoints and aggregate collections

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

Contract generates no code; NOF.Contract.SourceGenerator retains declaration analyzers. Application generates Service.Operation and a complete typed Descriptor. ServiceOperationReference and Descriptor.Reference are removed; remote clients use shared payloads and their own concrete integration ports. Contracts and IApplicationServiceImplementation need no partial modifier. Result and failure branches are generated in Application operations. Operation identity remains contract plus method. Registration captures static HandlerMappings, which are required; no service-container construction or instance-mapping fallback is used during resolution.

Register the application assembly and each service container once. Individual operations continue to be discovered automatically:

```csharp
builder.AddApplicationPart(typeof(MailService).Assembly);
builder.AddApplicationService<MailService>();
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq")!));
builder.Services.ConfigureRabbitMQ(endpoints =>
{
    endpoints.MapQueue<MailService.Send>("mail.send.v1");
    endpoints.MapQueue<OrderAudit.Record>("orders.audit.v1");
    endpoints.MapQueue<OrderProjection.Apply>("orders.projection.v1");
});
```

RabbitMQ maps existing queues only, while Memory owns its independent endpoint API. Exchange topology and publishing use native RabbitMQ APIs. Generic `MapQueue<Operation>(queue)` selects the generated descriptor; the explicit overload accepts only `ServiceOperationDescriptor<T, Empty>`. Registration rejects non-Empty outputs and declared failures. An operation returning a DTO or FailureCase union cannot silently discard that output through an asynchronous binding. Multiple operations can consume the same payload. Each subscription gets its own receipt, deduplication key and retries. Instances using the same subscription ID compete; distinct subscription IDs receive independent copies. Choose globally namespaced, stable subscription IDs; deployment/service names no longer implicitly prefix them.

## Calls and durable intents

InvokeAsync waits for an application operation to finish. Immediate remote calls use a concrete client. Transactional work uses IOutbox.EnqueueAsync(message, context, options, ct), followed by saving the same IUnitOfWork; its return value is an intent ID, not a remote business result. Infrastructure registers a stable message ID/version and IOutboxHandler<T>; the outbox knows neither queues nor service operations.

```csharp
await MailService.Send.InvokeAsync(services, mail, context, ct);
await memoryClient.SendAsync("mail.send.v1", mail, context, ct);
await outbox.EnqueueAsync(mail, context, cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

Inbound endpoints belong to concrete transport adapters. The shared MessageBinding API and producer RouteToQueue/Topic declarations are removed. One operation may receive multiple queues. Concrete clients name physical destinations explicitly. See [generic transactional outbox](transactional-outbox.md) for registration, typed adapters, retries and deployment migration.

## One execution boundary

`IOperationExecutor` executes a selected descriptor in the existing host scope and waits for completion. Local `IOperationInvoker` creates an isolated scope and copies verified identity; HTTP uses the request scope; Inbox uses its existing transaction scope so handler changes, receipt progress and newly staged messages commit together. No handler instance is constructed before invocation filters run.

Application-owned `InvocationContext` composes semantic `Context` and supplies Operation, InvocationId, Origin (Local/Http/Message), implementation policy metadata. Filters may replace semantic context through WithContext/WithTenantId; views preserve invocation identity; results flow through returns. `Context.Current` contains the semantic Context, not the InvocationContext. Pass `invocation.Context` to nested semantic APIs. Identity has one source, scoped IUserContext.

All origins run contract and implementation authorization policies. Message authentication does not deserialize producer claims or access tokens. Hosts establish their consumer identity through their selected pipeline/filter before authorization; an anonymous consumer cannot invoke a protected operation. Broker ingress and tenant headers must be restricted by the host's trust boundary. Strict tenant options apply to all operation origins.

HTTP status and challenge headers stay in HttpRequestInboundAdapter. RejectionReason identifies Policy/Authentication/Authorization without an HTTP status code in the execution context. Policy rejection is distinct from a completed business failure union. Technical exceptions propagate. Inbox records rejected operations as terminal failures; technical failures follow its retry policy. Transport acknowledgement follows durable receipt acceptance, not handler completion.

The memory and RabbitMQ transports use the same MessageInboxReceiver. It establishes the delivery tenant before resolving scoped persistence, and treats a database conflict as duplicate acceptance only after confirming the exact message ID/consumer receipt exists.

## Breaking migration and stored messages

TenantAuthorizationOptions.RequireAuthenticatedTenantForRequests and RejectMismatchedTenantForRequests are renamed to RequireAuthenticatedTenant and RejectMismatchedTenant; their policies now apply to every operation origin.

Replace old command/notification handlers with Empty-returning operations and explicit inbound Queue/Subscription bindings. InvocationContext plus Origin replaces the old context subclasses. Immediate calls use concrete clients; deferred APIs become IOutbox.EnqueueAsync and options. Old dispatcher and producer routing APIs have no compatibility adapter.

The generic outbox requires a schema migration after draining legacy records. Follow the [outbox migration procedure](transactional-outbox.md). Earlier inbox/broker route-name changes still require draining or an explicit old-to-new receipt and subscription mapping in every tenant database. Back up, preserve deduplication history, and never guess unresolved routes.

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

RabbitMQ maps operations only to existing queues; see the [native topology sample](rabbitmq-sample.md).
