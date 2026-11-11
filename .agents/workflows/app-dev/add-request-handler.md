---
description: Add a Context-aware request/response operation with NOF RPC contracts, generated clients, and explicit server registration
---

# Add a Request-Response Operation

## 1. Define the Contract

```csharp
using NOF.Contract;

[TransportOverHttp(HttpRpcStyle.ControllerRpc, "api/orders")]
public interface IOrderService : IApplicationService
{
    [HttpEndpoint(HttpVerb.Get, "get")]
    Result<GetOrderResponse> GetOrder(GetOrderRequest request);
}

public sealed record GetOrderRequest(string Id);
public sealed record GetOrderResponse(string Id, string Name);
```

An application service contract is local by default and may opt into one transport adapter. Every method takes one reference-type request and returns its declared non-task business value, including a DTO, nullable value or union. Contract methods do not take `Context`, `CancellationToken`, or an `Async` suffix; those appear on generated asynchronous APIs.

For JSON-RPC, use `[TransportOverHttp(HttpRpcStyle.JsonRpc, "/rpc/orders")]` and remove method-level `[HttpEndpoint]`. For in-process-only calls, use `[TransportOverMemory]` and remove `[HttpEndpoint]`.

## 2. Define the RPC Server Container

```csharp
using NOF.Application;

public partial class OrderService : ApplicationService<IOrderService>;
```

The `partial` modifier is required (`NOF300`). The generator creates one nested handler base per contract operation.

## 3. Implement the Generated Handler Base

```csharp
public sealed class GetOrder : OrderService.GetOrder
{
    public override Task<Result<GetOrderResponse>> HandleAsync(
        GetOrderRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(new GetOrderResponse(request.Id, "demo")));
    }
}
```

## 4. Register the Application Part and Server

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

var app = await builder.BuildAsync();
```

`AddApplicationPart(...)` runs source-generated initializers for handlers and other metadata. `AddApplicationService<T>()` registers the server and its transport metadata. During `BuildAsync()`, ASP.NET Core automatically maps controller/JSON-RPC endpoints; memory contracts are not exposed.

## 5. Call through the Generated Client

```csharp
var result = await orderServiceClient.GetOrderAsync(request, context, cancellationToken);
```

The contract package generates `IOrderServiceClient` and, for HTTP transport, `HttpOrderServiceClient`. The infrastructure generator creates the local client for a registered server. Both implementations use the same client interface and explicit `Context`.
