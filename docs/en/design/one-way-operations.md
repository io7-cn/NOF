# One-way operations and the uniform Inbox base

[简体中文](../../zh-CN/design/one-way-operations.md) · [Unified operations](unified-operations.md) · [Transactional outbox](transactional-outbox.md)

A Contract return type describes the reply capability, not just whether a payload is useful.

| Contract | Handler | Producer-facing business reply |
|---|---|---|
| void Send(Message) | Task HandleAsync(...) | None; explicitly one-way |
| Empty Update(Message) | Task<Empty> or Task<Result> with FailureCase | Payload-free success or declared failure |
| Dto Read(Query) | Task<Dto> or Task<Result> with FailureCase | Successful value or declared failure |

```csharp
public interface IMailService : IApplicationService
{
    void Send(SendMail request);
    [FailureCase("Denied")]
    Empty ChangeSettings(ChangeMailSettings request);
}

public partial class MailService : ApplicationService<IMailService>;
public sealed class SendMailHandler(IMailGateway gateway) : MailService.Send
{
    public override Task HandleAsync(SendMail request, InvocationContext invocation, CancellationToken ct)
        => gateway.SendAsync(request, ct);
}
```

Void generates OperationHandler<TRequest> and OperationDescriptor<TRequest>. Its response metadata is typeof(void), with no business cases, Result, failure branches or result-returning InvokeAsync. Handler completion is awaited by the execution host so transactions, retries and cancellation work correctly. It is never async void, an unobserved Task or an automatic Task.Run.

FailureCase on void is NOF220. The old NOF209 prohibition and its void-to-Empty code fix are removed. A reply descriptor cannot masquerade as void, and a void descriptor cannot masquerade as Empty. The reply-producing IOperationExecutor.ExecuteAsync rejects void; a host uses OperationExecutor.ExecuteInScopeAsync to await processing without exposing its bookkeeping to the producer.

## Acceptance and execution are separate

```csharp
services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<MailService.Send>("mail.send.v1"));
services.AddMemoryMessaging(memory => memory.MapQueue<MailService.Send>("mail.send.v1"));
```

Queue/subscription endpoints accept only one-way handlers: void Contract operations or standalone OperationHandler<TRequest> classes. Even Empty without FailureCase is reply-producing and cannot be silently discarded. Concrete clients select native destinations; IOutbox stages integration intents. Sending completes according to the concrete client's acceptance contract, not the consumer's business result. Outbox delivery metadata preserves identity across retries; it does not imply exactly-once side effects.

Inbox receipt acceptance persists before broker acknowledgement. Worker execution later awaits the full handler Task, saves completion and commits the existing transaction. A policy/filter rejection is recorded as terminal failure; technical failures roll back and follow retry policy. Consumer failures are observable through receipt state/logging and application-owned follow-up messages, never a reply over the original send channel. Cancellation remains a host processing signal.

## Uniform Inbox execution

IInboxHandler and OperationInboxHandler are removed. Transport adapters register only InboxEndpoint(Id, Operation) metadata in the host-local InboxEndpointRegistry. Id is an opaque, stable receipt key, not interpreted by Inbox as queue/exchange/topic structure. The worker resolves the descriptor, deserializes its declared input and directly calls the common OperationExecutor.ExecuteInScopeAsync in the existing tenant scope and transaction.

The base retains persistence, exact receipt deduplication, owner fencing, recovery, cleanup and retries. Adapters retain topology and delivery acknowledgement; no alternate callback/handler interface is introduced. Custom stores still implement IInboxStore. A custom adapter can register an opaque endpoint and use MessageInboxReceiver without creating a second business handler type.

Memory and RabbitMQ may map the same visible queue name to different operations because persisted keys remain adapter-namespaced. An operation may have several inbound receipt keys. Duplicate keys and non-void targets fail configuration. Missing mappings for persisted receipts fail processing explicitly; never guess a CLR type from persisted payloads.

## HTTP and migration

Normal MapGet/MapPost operation response mappings reject void. A native HTTP acceptance endpoint can authorize the submitter, persist an outbox intent or durably accept a receipt, then return a transport acknowledgement such as 202. This acknowledges acceptance only; it neither executes the handler as a request/reply operation nor returns its failures. Do not return 202 after merely launching an in-process Task. Keep native authorization and tenant selection at the entry; queued execution uses a host-established trusted consumer identity, without persisting producer tokens/claims.

Change queued Contract returns from Empty to void and handlers from Task<Empty> to Task. Keep existing REST/local Empty operations and their FailureCase results unchanged. Use OperationDescriptor<TRequest> for explicit queue mappings; generic MapQueue<Operation> stays the same. Remove keyed IInboxHandler registrations; normal adapters register the uniform metadata automatically. Update runtime, generator, code-fix, hosting and adapter packages together.

The one-way/Inbox change itself needs no schema change; the accompanying Outbox cleanup requires the reviewed SimplifyOutboxMetadata migration. Preserve receipt/message IDs, payload JSON and stable adapter-prefixed receipt keys so pending work and deduplication history remain valid. Previously custom handler payloads must now match a declared one-way request; drain or explicitly migrate incompatible records before upgrading. Do not reinterpret arbitrary old bytes as a new DTO.

## Standalone application reactions

ServiceOperation is renamed OperationHandler. An internal reaction can be a concrete class without a Contract interface, partial modifier or attribute:

```csharp
public sealed class AwardWelcomePoints(IPoints points) : OperationHandler<UserCreated>
{
    public override Task HandleAsync(UserCreated message, InvocationContext context, CancellationToken ct)
        => points.AddAsync(message.UserId, 100, ct);
}

builder.AddApplicationPart(typeof(AwardWelcomePoints).Assembly);
builder.Services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<AwardWelcomePoints>("user.welcome-points"));
builder.Services.AddInvocationFilters(filters => filters.UseCase<AwardWelcomePoints>().Use<AuditFilter>());
```

The Application generator captures scoped registration and typed descriptor metadata without constructing handlers. No AddApplicationService or per-handler registration is needed for this form. A standalone OperationHandler<TRequest,TResponse> also supports typed HTTP mapping and local reply execution, including nullable values and user-authored business unions. FailureCase-generated Result branches remain a Contract-method feature.

Standalone identity is the handler's full CLR name; two handlers may share the same input/output types. It has no public Contract and receives no inferred Contract permissions. Global/operation filters and host tenant/custom authorization constraints still apply. Service-level filters apply only to Contract-backed operations. Discovery never exposes HTTP routes automatically. Keep internal reactions behind trusted consumer configuration; a public HTTP mapping still needs explicit native authorization as appropriate.

CountOrder in the runnable Notifications sample demonstrates this form; it is absent from IMessageDemoService and maps directly with MapQueue<CountOrder>. Durable Inbox receipt keys remain independent of handler identity, preserving existing queue receipts during this refactoring.

The two executor services are merged. IOperationExecutor is the renamed reply-producing local interface; OperationExecutor is the single implementation. Hosts call ExecuteInScopeAsync on that same implementation to retain the HTTP/Inbox scope, identity and transaction. IOperationInvoker and the old separate executor interface are removed, with no compatibility layer.
