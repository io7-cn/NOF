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
app.MapOrderEndpoints();
app.MapOpenApi();
await app.RunAsync();
```

Use native Build(); app.UseNOF() installs scoped ambient activation, and ordinary app.MapOrderEndpoints() explicitly maps feature routes. Startup I/O uses native hosted services.

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

## Immediate Calls and Transactional Intents

```csharp
await client.SendAsync("mail.send.v1", message, context, cancellationToken);
await outbox.EnqueueAsync(new OrderCreatedNotification(order.Id), context,
    cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

The client is concrete. IOutbox only stages; the host enables AddOutbox() and adds the assembly whose OutboxHandler<T> classes declare schema constants; generated application-part registrations bind the adapters. Explicit transactions require commit. See docs/en/design/transactional-outbox.md.

## Server Operation Call

```csharp
var result = await OrderService.GetOrder.InvokeAsync(services, request, context, cancellationToken);
```

This generated server entry runs authorization, tenant and invocation-scope policies.
No client interface or HTTP/Local Client is generated. Remote callers maintain their
own HTTP integration, as demonstrated by the sample UI API ports/adapters.

## HTTP endpoint feature

Use an ordinary MapOrderEndpoints extension returning RouteGroupBuilder. Map with
`group.MapGet<OrderService.GetOrder>("/{id}")` and typed Request/Response rules.
Configure `.Response<OrderService.GetOrder.NotFound>(404)`; use a typed lambda returning complete TypedResults for projection/Location.
Call the feature extension after Build. No AutoInject module, discovery or mandatory
endpoint filter is needed. Context values are explicit; business context never flows back. Response maps replace as a whole; no header/body/Problem field DSL, variants, selector or HTTP Filter view remains.
Runtime OpenAPI uses the same final binding; source generation only retains request types.
See docs/en/design/http-adaptation.md and configuration-rest.md.
