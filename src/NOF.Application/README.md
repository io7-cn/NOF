# NOF.Application

Application layer package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

Contains the application service abstractions used to implement NOF applications: RPC servers, request handlers, command handlers, notification handlers, mapping, caching, object storage, and persistence contracts.

This package does not define runtime outbound authentication directives.

`AddNOFApplication()` registers the package-local application defaults, including expression-based mapping and the package-local Domain defaults.

Commands and notifications are plain payload types. Handler discovery comes from the `CommandHandler<T>` and `NotificationHandler<T>` base classes rather than marker interfaces on the message types.

## Application services

IApplicationService contracts and ApplicationService<TService> containers keep generated Service.Operation overrides. ServiceOperation<TRequest,TResponse> accepts ordinary business responses. IRpcResponse is removed. Generated descriptors identify operations for HTTP adapters without introducing an HTTP dependency. Application-part generation registers operation implementations; service containers register with AddApplicationService<TService>(). Client generation is removed. Call Service.Operation.InvokeAsync(services, request, context, ct) on the server; maintain HTTP integrations in the calling application. See the [migration guide](../../docs/en/design/application-requests.md).

## Key Abstractions

### RPC Servers

RPC contracts are declared on `IApplicationService` interfaces in the contract layer. Application implementations use `ApplicationService<TService>`:

```csharp
using NOF.Application;
using NOF.Contract;

public partial interface IOrderService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    OrderDto GetOrder(GetOrderRequest request);
}

public partial class OrderService : ApplicationService<IOrderService>;

public sealed class GetOrder : OrderService.GetOrder
{
    private readonly IDbContext _dbContext;

    public GetOrder(IDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task<Result> HandleAsync(GetOrderRequest request, Context context, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Set<Order>()
            .FirstOrDefaultAsync(entity => entity.Id == request.Id, cancellationToken);
        if (order is null)
        {
            return new NotFound("Order not found");
        }

        return new OrderDto(order.Id, order.Status);
    }
}
```

Call `OrderService.GetOrder.InvokeAsync(services, request, context, cancellationToken)` on the server. This resolves `IApplicationServiceInvoker`, preserves application policies and opens an invocation scope. No client interface, Local Client or HTTP Client is generated.

Streaming operations may directly return IAsyncEnumerable<T> when they declare no initial failure:

```csharp
public partial class OrderService : ApplicationService<IOrderService>;

public sealed class Watch : OrderService.Watch
{
    public override Task<IAsyncEnumerable<OrderEvent>> HandleAsync(WatchOrdersRequest request, Context context, CancellationToken cancellationToken)
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

### Command Handlers

```csharp
public record SendEmailCommand(string To, string Subject, string Body);

public sealed class SendEmailHandler : CommandHandler<SendEmailCommand>
{
    public override Task HandleAsync(SendEmailCommand command, Context context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

### Notification Handlers

```csharp
public record OrderCreatedNotification(Guid OrderId);

public sealed class OrderCreatedHandler : NotificationHandler<OrderCreatedNotification>
{
    public override Task HandleAsync(OrderCreatedNotification notification, Context context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

### Transactional Message Sending

Use `ICommandSender` and `INotificationPublisher` for both immediate and deferred dispatch:

The [compiled sample](../../sample/NOF.Sample.Application/Integration/TransactionalMessages.cs)
contains the complete deferred-dispatch example.

```csharp
await _notificationPublisher.DeferPublishAsync(new OrderCreatedNotification(order.Id), context, cancellationToken);
await _commandSender.DeferSendAsync(new SendEmailCommand(order.Email, "Created", "Order created."), context, cancellationToken);
await _dbContext.SaveChangesAsync(cancellationToken);
```

Pass the handler's explicit `Context`. Deferred dispatch runs the outbound middleware and stores
its resulting headers and trace parent in the outbox; arbitrary context items and the authenticated
principal are not automatically serialized. Tenant-header middleware uses the explicit context's
tenant item, falling back to `Context.Current.TenantId`. Pass the authorized handler context,
and validate tenant access at public boundaries; passing `Context` alone does not establish tenant trust. Identity
propagation requires an explicit outbound authentication policy. See the
[tenant authorization](../NOF.Infrastructure/README.md#tenant-authorization) for the trust boundaries.

With an explicit transaction, `SaveChangesAsync` writes inside that transaction and
`CommitAsync` makes it durable. After rollback or an uncertain commit, dispose the scope and
recover using a fresh context and an application idempotency key. See
[persistence boundaries](../../docs/design/repository-uow.md).

### Persistence Abstractions

Application code should depend on `IDbContext` from `NOF.Application`, repository abstractions from `NOF.Domain`, and async query helpers under `NOF.Application` rather than a concrete ORM type.

```csharp
using NOF.Application;
using NOF.Domain;

var exists = await _dbContext.Set<Order>()
    .AsNoTracking()
    .AnyAsync(order => order.Id == request.Id, cancellationToken);
```

The async query surface is exposed through `IAsyncQueryable<T>` in `NOF.Domain` plus extension methods such as `AnyAsync`, `CountAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `ToListAsync`, `SumAsync`, and `AverageAsync`. Concrete infrastructure adapters decide how those terminal operations are executed.

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
var orders = await _dbContext.Set<Order>()
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

This registers `MappingRegistry`, the default `ExpressionMapper` implementation of `IMapper`, and a scoped daemon that binds `Mapper.Current` to the current async flow.

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

[FailureCase declarations](../../docs/en/design/expected-failures.md) expand operation return types into Contract-owned closed outcomes; registration, descriptors and local invocation all use that generated response type.

Generated operations inherit Contract-owned operation bases. Handler overrides use
Task<Result> and new NotFound(...) with the original Contract case identity.
No Create<Name> factories or custom expected-outcome analyzer are generated.
