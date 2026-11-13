# NOF Recipes

## Program Bootstrap

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnStart();

var app = builder.Build();
app.UseNOF();
app.MapNOFEndpoints();
app.MapOpenApi();
await app.RunAsync();
```

Use native Build(); app.UseNOF() installs HTTP middleware, and app.MapNOFEndpoints() explicitly maps registered IHttpEndpointModule instances. Startup I/O uses native hosted services.

## Application Service Contract + Handler

```csharp
using NOF.Application;
using NOF.Contract;

public interface IOrderService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    GetOrderResponse GetOrder(GetOrderRequest request);
}

public partial class OrderService : ApplicationService<IOrderService>;

public sealed class GetOrder : OrderService.GetOrder
{
    public override Task<Result> HandleAsync(
        GetOrderRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Result>(new GetOrderResponse(request.Id, "sample"));
    }
}
```

## Provider-Neutral Query

```csharp
[Mappable<Order, GetOrderResponse>]
public static partial class Mappings;

public sealed class GetOrder(IOrderQueries queries) : OrderService.GetOrder
{
    public override async Task<Result> HandleAsync(
        GetOrderRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var response = await queries.FindAsync(request.Id, cancellationToken);

        return response is null
            ? new NotFound("Order not found")
            : response;
    }
}
```

Register the source/destination pair with `[Mappable<,>]`, then filter, order, and page before `ProjectTo<TDestination>()`.

## Domain Update

```csharp
var order = await orders.GetAsync(request.Id, cancellationToken);

order!.Confirm();
await unitOfWork.SaveChangesAsync(cancellationToken);
```

## Access User and Tenant

```csharp
public sealed class MyHandler(IUserContext userContext)
{
    public string? CurrentUserId => userContext.User.Id;
    public string GetTenantId(Context context) => context.TenantId;
}
```

## Immediate and Deferred Messaging

```csharp
await commandSender.SendAsync(command, context, cancellationToken);
await notificationPublisher.PublishAsync(notification, context, cancellationToken);

await notificationPublisher.DeferPublishAsync(
    new OrderCreatedNotification(order.Id),
    context,
    cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

Deferred messages are staged in the current unit of work; IUnitOfWork.SaveChangesAsync saves them with aggregate and projection changes on the same scoped provider context. Explicit transactions commit separately.

## Server Operation Call

```csharp
var result = await OrderService.GetOrder.InvokeAsync(services, request, context, cancellationToken);
```

This generated server entry runs authorization, tenant and invocation-scope policies.
No client interface or HTTP/Local Client is generated. Remote callers maintain their
own HTTP integration, as demonstrated by the sample UI API ports/adapters.

## HTTP endpoint module

Use IHttpEndpointModule with AutoInject(ServiceLifetime.Singleton). Map native ASP.NET
Core endpoints, construct business requests from HTTP inputs and call
http.InvokeAsync(Service.Operation.Descriptor, request, ct). Use
ServiceRejectionHttpFilter for policy rejection and feature-owned TypedResults for
business failures. Legacy HttpEndpoint/transport annotations and RPC adapters are deleted.
