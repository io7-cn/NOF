# One-way operations and direct message endpoints

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

## Direct endpoint execution

MapQueue accepts only void operations without business FailureCase declarations. The adapter creates a fresh scope, restores semantic Context before constructing dependencies, and calls OperationExecutor.ExecuteInScopeAsync with InvocationOrigin.Message. Authorization and Global/Service/UseCase filters remain effective; rejection is a failed delivery, not an ACK. There is no receipt storage, deduplication, polling or implicit SaveChanges/transaction. Application handlers or filters own their commit boundary. RabbitMQ ACK follows awaited execution; native retry/dead-letter settings handle failures. A lost ACK after a committed effect can run the operation again, so business idempotency is required.

Memory SendAsync awaits local execution and propagates failure/cancellation. PublishAsync executes each local subscription independently. Neither direct Memory calls nor NOF store unfinished consumer work across process restarts. Awaiting execution does not make void a reply contract: no Result/business failure is returned to the sender.

## HTTP and migration

Empty remains reply-producing and can carry FailureCase unions; void has no result-returning invocation API. Reply-producing HTTP/local invocation rejects void. An asynchronous HTTP acceptance endpoint must use an actual durable broker or an explicit application-owned durable mechanism before returning 202; Task.Run and direct Memory dispatch do not promise durable acceptance.

Remove Inbox registrations, custom stores, receipt metadata and scheduling options. Producers explicitly select AddOutbox and flat OutboxOptions. Pure consumers register only transport endpoints and business dependencies. Resolve old ACKed-but-pending Inbox work before switching binaries; the guarded RemoveInbox migration refuses unfinished rows. See [Outbox and migration](transactional-outbox.md).

## Standalone application reactions

Concrete operations need no Contract method or partial modifier:

```csharp
public sealed class CountOrder(IOrderMetrics metrics) : OperationHandler<OrderCreated>
{
    public override Task HandleAsync(OrderCreated request, InvocationContext context, CancellationToken ct)
        => metrics.RecordOnceAsync(request.OrderId, ct);
}

builder.AddApplicationPart(typeof(CountOrder).Assembly);
builder.Services.AddRabbitMQ(...)
    .ConfigureRabbitMQ(rabbit => rabbit.MapQueue<CountOrder>("orders.metrics"));
```

AddApplicationPart discovers typed descriptors and scoped implementations. The sample's CountOrder uses an atomic unique business key through its projection port. ContractType is null for standalone operations; only Contract interfaces/methods declare permissions. Global and UseCase filters still apply. IOperationExecutor.ExecuteAsync is the local reply boundary; host adapters use the same concrete OperationExecutor.ExecuteInScopeAsync without introducing another executor or handler interface.
