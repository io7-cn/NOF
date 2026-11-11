# NOF.Contract

Contract layer package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

Defines the messaging contracts and shared models that form the public API surface of your application. This package contains `Result<T>`, `Result<IAsyncEnumerable<T>>`, `Empty`, HTTP endpoint annotations, and other shared attributes used by source generators and hosts.

`Context` remains a generic execution-context carrier for explicit metadata passing across boundaries.

## Key Abstractions

### Messages

```csharp
// Request with response
public record GetOrderRequest(Guid Id);

// Request without payload response
public record ArchiveOrderRequest(Guid Id);

// Fire-and-forget command
public record SendEmailCommand(string To, string Subject, string Body);

// Publish/subscribe notification
public record OrderCreatedNotification(Guid OrderId);
```

### Host-Tenant RPC Operations

Use `[UseHostTenant]` on an RPC contract method to execute the operation in the host tenant:

```csharp
[TransportOverMemory]
public interface IPlatformUserService : IApplicationService
{
    [UseHostTenant]
    [RequirePermission("platform.users.read")]
    Result<UserProfile> GetUser(GetUserRequest request);
}
```

The default NOF request pipeline authorizes the caller normally, then selects the host tenant before resolving the handler and its dependencies. This applies to HTTP and in-process RPC. The method's explicit `Context` and `Context.Current` both use the host tenant; the caller's context is restored after the invocation. The attribute selects execution tenancy and does not grant host permissions or bypass authorization.

Host-tenant selection runs in the application inbound policy for both local and HTTP calls. The metadata key is `nof.tenant.use_host`; `[Metadata(UseHostTenantAttribute.MetadataKey, "true")]` is equivalent. Unmarked methods retain normal tenant selection.

### Result Type

```csharp
// Success
return Result.Success(orderDto);

// Failure
return Result.Fail("404", "Order not found");
```

`Result<T>` is a native `union (T, NOF.Abstraction.Failure)`: successful values are
returned and matched directly. `Success<T>` is optional; use `Result<Success<T>>`
or `Result.WithMessage(data, message)` when a successful response needs a message.
`Failure(code, message)` is shared with Domain. IsSuccess, Extra and reflective
result projections have been removed. See the [migration guide](https://github.com/io7-cn/NOF/blob/main/docs/en/design/result-unions.md)
([简体中文](https://github.com/io7-cn/NOF/blob/main/docs/zh-CN/design/result-unions.md)).

### Streaming Result Type

Use `Result<IAsyncEnumerable<T>>` when an RPC method returns a server-side stream. This keeps the contract surface synchronous while generated server invocation returns `Task<Result<IAsyncEnumerable<T>>>`. No client is generated.

```csharp
public record WatchOrdersRequest(Guid CustomerId);
public record OrderEvent(Guid OrderId, string Status);

[TransportOverHttp(HttpRpcStyle.ControllerRpc)]
public interface IOrderService : IApplicationService
{
    [HttpEndpoint(HttpVerb.Get, "/api/orders/watch")]
    Result<IAsyncEnumerable<OrderEvent>> Watch(WatchOrdersRequest request);
}
```

### RPC Contracts

RPC service methods use a strict single-request signature, do not accept `CancellationToken` on the contract surface,
do not end with `Async`, and return a declared non-Task business value, including a DTO, nullable value or union. Use a DTO directly or choose `Result<T>` when the operation has a business failure case; `Result` remains useful when no response payload is needed. `Empty` can be a request type but is not an RPC return type. Streaming methods must return `Result<IAsyncEnumerable<T>>`.

```csharp
public record GetOrderRequest(Guid Id);
public record CreateOrderRequest(string ProductName, int Quantity);

[TransportOverHttp(HttpRpcStyle.ControllerRpc)]
public interface IOrderService : IApplicationService
{
    [Summary("Get order")]
    [HttpEndpoint(HttpVerb.Get, "/api/orders/get")]
    Result<OrderDto> Get(GetOrderRequest request);

    [Summary("Create order")]
    [HttpEndpoint(HttpVerb.Post, "/api/orders")]
    Result<OrderDto> Create(CreateOrderRequest request);

    [Summary("Archive order")]
    [HttpEndpoint(HttpVerb.Post, "/api/orders/archive")]
    Result Archive(ArchiveOrderRequest request);

    [Summary("Watch order events")]
    [HttpEndpoint(HttpVerb.Get, "/api/orders/watch")]
    Result<IAsyncEnumerable<OrderEvent>> Watch(WatchOrdersRequest request);
}
```

`TransportOverHttp` supplies server adapter metadata only. NOF generates no client
interfaces, HTTP proxies or Local Clients. Remote callers maintain their own HTTP
integration; referencing Contract alone does not supply a network client.

For in-process services, omit transport attributes or explicitly use
`[TransportOverMemory]`. Register `builder.AddApplicationService<OrderService>()`
and call `OrderService.Get.InvokeAsync(services, request, context, ct)` through the
generated server operation. HTTP annotations are not required for this path.

See the [application service migration guide](../../docs/en/design/application-requests.md).

### Other Annotations

- **`[HttpEndpoint]`** - declares HTTP verb and route metadata for RPC methods
- Application service contracts may declare at most one `TransportOverAttribute` implementation; without one they remain local-only
- **`[TransportOverHttp]`** - declares the HTTP RPC style and an optional contract-level route prefix; emits server binding metadata only
- **`[TransportOverMemory]`** - declares that the RPC service is intended for in-process server invocation; the built-in HTTP transport does not expose it, and methods must not declare `[HttpEndpoint]`
- HTTP route prefixes are application-relative paths. A leading `/` is optional and one trailing `/` is normalized away; empty or whitespace values, absolute URIs, query strings, fragments, route parameters, escaped characters, backslashes, empty path segments, and `.` or `..` segments are rejected at compile time
- JSON-RPC contracts use the service route prefix and operation names; when the prefix is omitted, the endpoint is `/`
- JSON-RPC methods must not declare `[HttpEndpoint]`
- JSON-RPC streaming methods use SSE; each event carries a JSON-RPC response envelope for one stream item
- Controller RPC operation routes support required path segments such as `{id}`. They match request properties by CLR name, ignoring case. Other properties are bound from the query or JSON body according to the HTTP verb
- Streaming HTTP endpoints use server-sent events when hosted by `NOF.Hosting.AspNetCore`
- **`[RequirePermission]`** - declares required permissions for an endpoint
- **`[Summary]`** - adds summary documentation to generated endpoints
- These NOF-specific attributes are all metadata-backed and converge on `MetadataAttribute`

## Context

Use `Context` for explicit per-call metadata. `Context.TenantId` exposes the normalized effective tenant, while `WithTenantId(...)` returns a new context with a tenant value. `Context.Current` exposes the context bound to the current async flow and falls back to `Context.Empty`; handlers should prefer the context passed to them explicitly. Header snapshots may be copied into `Context.Items` by transport/infrastructure components. String item keys use ordinal, case-insensitive comparison to preserve HTTP header semantics; non-string keys retain their default equality behavior.

Runtime outbound authentication directives are provided by `NOF.Application`, not `NOF.Contract`.

## Installation

```shell
dotnet add package NOF.Contract
```

## License

Apache-2.0
