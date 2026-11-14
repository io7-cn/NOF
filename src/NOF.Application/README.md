# NOF.Application

Application layer package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

Contains the application service abstractions used to implement NOF applications: service operations and message submission, mapping, caching, object storage, and persistence contracts.

This package does not define runtime outbound authentication directives.

`AddNOFApplication()` registers the package-local application defaults, including expression-based mapping and the package-local Domain defaults.

All consumers implement generated ApplicationService operations. The host binds stable queues/subscriptions to Empty-returning operation descriptors. Plain contracts need no partial modifier; descriptors are generated in a separate Contract class.

## Application services

IApplicationService contracts and ApplicationService<TService> containers keep generated Service.Operation overrides. ServiceOperation<TRequest,TResponse> accepts ordinary business responses. IRpcResponse is removed. Generated descriptors identify operations for HTTP adapters without introducing an HTTP dependency. Application-part generation registers operation implementations; service containers register with AddApplicationService<TService>(). Client generation is removed. Call Service.Operation.InvokeAsync(services, request, context, ct) on the server; maintain HTTP integrations in the calling application. See the [migration guide](../../docs/en/design/application-requests.md).

## Key Abstractions

### RPC Servers

RPC contracts are declared on `IApplicationService` interfaces in the contract layer. Application implementations use `ApplicationService<TService>`:

```csharp
using NOF.Application;
using NOF.Contract;

public interface IOrderService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    OrderDto GetOrder(GetOrderRequest request);
}

public partial class OrderService : ApplicationService<IOrderService>;

public sealed class GetOrder : OrderService.GetOrder
{
    private readonly IOrderQueries _orders;

    public GetOrder(IOrderQueries orders)
    {
        _orders = orders;
    }

    public override async Task<Result> HandleAsync(GetOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        var order = await _orders.FindAsync(request.Id, cancellationToken);
        if (order is null)
        {
            return new NotFound("Order not found");
        }

        return new OrderDto(order.Id, order.Status);
    }
}
```

Call `OrderService.GetOrder.InvokeAsync(services, request, context, cancellationToken)` on the server. This resolves `IOperationInvoker`, preserves application policies and opens an invocation scope. No client interface, Local Client or HTTP Client is generated.

Streaming operations may directly return IAsyncEnumerable<T> when they declare no initial failure:

```csharp
public partial class OrderService : ApplicationService<IOrderService>;

public sealed class Watch : OrderService.Watch
{
    public override Task<IAsyncEnumerable<OrderEvent>> HandleAsync(WatchOrdersRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(Stream());

        async IAsyncEnumerable<OrderEvent> Stream()
        {
            yield return new OrderEvent(Guid.NewGuid(), "Created");
            await Task.Delay(1000, cancellationToken);
            yield return new OrderEvent(Guid.NewGuid(), "Shipped");
        }
    }
}
```

The contract surface for the same method remains `IAsyncEnumerable<OrderEvent> Watch(WatchOrdersRequest request);`.

### Message consumers and transactional intents

Transport-owned queue/subscription endpoints target ordinary Empty-returning application operations. Immediate remote calls use concrete clients. Application stages generic messages through IOutbox:

```csharp
await outbox.EnqueueAsync(new OrderCreated(order.Id), invocation.Context, cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

The host registers stable message IDs/versions and IOutboxHandler<T> adapters. Enqueue snapshots without sending or saving; the returned Guid is an intent ID. Optional generic headers are snapshotted with the payload; ordering belongs to infrastructure. No IMessageDispatcher, deferred Send/Publish family or operation-based producer routing remains. See [transactional outbox](../../docs/en/design/transactional-outbox.md) and the [compiled sample](../../sample/NOF.Sample.Application/Services/MessageDispatchService.cs).

### Persistence boundaries

Declare aggregate-specific repositories in Domain and DTO query interfaces in Application.
Use IUnitOfWork.SaveChangesAsync to save staged aggregate, projection and outbox changes
through one scoped persistence context. Optional ITransactionManager exposes async commit
and rollback. IDbContext, generic repositories, async query executors and update expressions
are removed from core. Native queries and ORM state belong in infrastructure implementations.
See [migration and transaction semantics](../../docs/en/design/persistence-boundaries.md).

### Object Mapping (`IMapper`)

NOF uses expression-based mappings for both database projection and in-memory conversion:

```csharp
[Mappable<Order, OrderDto>]
[Mappable<Order, OrderSummary>]
public static partial class Mappings;
```

The generator writes `MappingRegistration` expressions into `MappingRegistry`.
Those mappings become available once the assembly is added via `AddApplicationPart(...)`. Every direction must be declared explicitly, and the generator reports an error instead of falling back to an opaque runtime mapping.

Apply a generated mapping directly to a query after filtering, ordering, and paging:

```csharp
// Inside a persistence/query adapter using its native query source:
var orders = await orderQuery
    .AsNoTracking()
    .Where(order => order.IsActive)
    .ProjectTo<OrderDto>()
    .ToListAsync(cancellationToken);
```

`ProjectTo<TDestination>()` receives an `IQueryable`, discovers the source type from `ElementType`, and resolves the expression through the ambient `Mapper.Current`. Each registration carries an AOT-safe generic query applicator, so no runtime generic method construction is required. Use `ProjectTo<TDestination>(mapper)` when an explicit mapper boundary is preferable.

Diagnostic `NOF025` warns when filtering, ordering, paging, set operations, another projection, or predicate-bearing terminal operations are composed after `ProjectTo`. Plain materialization and client-side work after `AsEnumerable` are not reported.

Diagnostic `NOF026` warns for `Queryable.Select(x => mapper.Map<TSource, TDestination>(x))`; use `ProjectTo<TDestination>(mapper)` so the mapping expression can be translated by the query provider.

For an already materialized value, `IMapper.Map<TSource, TDestination>(source)` compiles and caches the same expression used by `ProjectTo`.
Custom pure expressions can be registered with `services.AddMapping<TSource, TDestination>(expression)`.

For package-local defaults:

```csharp
services.AddNOFApplication();
```

This registers MappingRegistry and the default ExpressionMapper. Managed entries or an explicit services.BindNOF(context) scope bind and restore Mapper.Current; ordinary DI scopes have no activation requirement. See [ambient binding](../../docs/en/design/ambient-binding.md) ([简体中文](../../docs/zh-CN/design/ambient-binding.md)).

`AddNOFApplication()` already includes `AddNOFDomain()`:

```csharp
services.AddNOFApplication();
```

If you want to override the default `IIdGenerator`, register your own implementation explicitly:

```csharp
services.AddNOFApplication();
services.AddSingleton<IIdGenerator, MyIdGenerator>();
```

## Installation

```shell
dotnet add package NOF.Application
```

`ICacheService` also implements `IDistributedCache`, so standard distributed cache consumers can resolve either abstraction from NOF cache registrations.

### Object Storage

Application code can use `IObjectStorage` without depending on a cloud SDK. The common surface supports uploads, streaming reads, metadata, existence checks, deletes, server-side copies, and prefix-based enumeration:

```csharp
await using var content = File.OpenRead("invoice.pdf");
var stored = await objectStorage.PutAsync(
    "documents",
    $"invoices/{invoiceId}.pdf",
    content,
    new ObjectStorageWriteOptions { ContentType = "application/pdf" },
    cancellationToken);

var result = await objectStorage.OpenReadAsync(
    stored.BucketName,
    stored.ObjectKey,
    cancellationToken);
if (result.HasValue)
{
    await using var objectContent = result.Value.Content;
    // Stream objectContent to the caller or another destination.
}
```

The host selects an `IObjectStorageRider`; application handlers continue to depend only on `IObjectStorage`.

## License

Apache-2.0

[FailureCase declarations](../../docs/en/design/expected-failures.md) stay on Contract methods. Application generates closed outcomes inside each operation; registration, descriptors and local invocation use that generated response type. Handler overrides use `Task<Result>` and `new NotFound(...)`. No case factories or custom expected-outcome analyzer are generated.

### Invocation extensions

`IInvocationFilter` is the unified extension contract for service operations, commands and notifications. Registration through `AddInvocationFilter<T>()` declares an extension; the host chooses `IInvocationPipeline` to execute it. The optional in-memory default lives in Infrastructure. See [host-selected execution](../../docs/en/design/invocation-extensions.md).

FailureCase annotations stay on non-partial Contract methods; this package generates Service.Operation.Result and its concrete failure cases. Application owns the single complete operation descriptor. See [expected failures](../../docs/en/design/expected-failures.md).

Execution models, descriptors and policy interpretation belong to this package. ServiceOperationDescriptor is the sole operation metadata model; input-only Reference and instance mapping/resolver fallbacks are removed; Contract holds declarations and data only and generates no code; semantic Context belongs to Abstraction. See [Contract boundaries](../../docs/en/design/contract-boundaries.md).
