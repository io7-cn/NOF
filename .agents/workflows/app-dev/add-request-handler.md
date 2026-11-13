---
description: Add a Context-aware request/response operation with NOF application service contracts, generated server operations, and explicit service registration
---

# Add a Request-Response Operation

## 1. Define the Contract

```csharp
using NOF.Contract;

public interface IOrderService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    GetOrderResponse GetOrder(GetOrderRequest request);
}

public sealed record GetOrderRequest(string Id);
public sealed record GetOrderResponse(string Id, string Name);
```

An application service contract is local by default and may opt into one transport adapter. Every method takes one reference-type request and returns its declared non-task business value, including a DTO, nullable value or union. Contract methods do not take `Context`, `CancellationToken`, or an `Async` suffix; those appear on generated asynchronous APIs.

Keep HTTP routing in an IHttpEndpointModule, outside Contract. See docs/en/design/configuration-rest.md for the complete CRUD slice.

## 2. Define the Application Service Container

```csharp
using NOF.Application;

public partial class OrderService : ApplicationService<IOrderService>;
```

The `partial` modifier is required (`NOF300`). The generator creates one nested handler base per contract operation.

## 3. Implement the Generated Handler Base

```csharp
public sealed class GetOrder : OrderService.GetOrder
{
    public override Task<Result> HandleAsync(
        GetOrderRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Result>(new GetOrderResponse(request.Id, "demo"));
    }
}
```

## 4. Register the Application Part and Server

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

var app = builder.Build();
app.UseNOF();
app.MapNOFEndpoints();
```

`AddApplicationPart(...)` runs source-generated initializers for handlers and other metadata. `AddApplicationService<T>()` registers the service container. After native Build(), app.MapNOFEndpoints() explicitly maps registered IHttpEndpointModule instances. Use HttpContext.InvokeAsync(Service.Operation.Descriptor, request, ct) at the HTTP boundary.

## 5. Invoke the Server Operation

```csharp
var result = await OrderService.GetOrder.InvokeAsync(services, request, context, cancellationToken);
```

The generated static entry resolves IApplicationServiceInvoker and preserves shared
policies and the fresh inbound scope. No client interface, HTTP Client or Local Client
is generated. Native HTTP endpoints call this entry and return ASP.NET Core results;
remote consumers maintain their own HTTP integrations. For tests, use
CallAsync<TResult>((services, context, ct) => OrderService.GetOrder.InvokeAsync(...)).
