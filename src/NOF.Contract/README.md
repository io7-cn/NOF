# NOF.Contract

Transport-independent application service declarations, messages and business outcomes.

```csharp
public interface IOrderService : IApplicationService
{
    Result<OrderDto> Get(GetOrderRequest request);
}
```

Declare one reference-type request and a non-Task business return. Do not declare
Context/CancellationToken/Async on the proto interface. Generated server operations
receive those execution arguments. DTOs, nullable values and business unions work
without response marker interfaces. No client code is generated.

`Result<T>` is native `union (T, NOF.Abstraction.Failure)`. Match successful T directly;
use optional `Success<T>` only when successful message metadata is needed. Result has
no IRpcResponse interface or empty-response factory. Policy rejection belongs to the
execution outcome, separately from returned business failures.

HttpEndpoint, HTTP verbs, transport annotations and generated HTTP route metadata
have moved to NOF.Hosting.AspNetCore. Contracts need no Hosting reference. Native HTTP
modules own routing, binding and status mappings. Legacy protocol reader utilities
accept explicit failure/empty-body conversion delegates; they impose no business
outcome interface. Context remains explicit execution metadata; application permission
and host-tenant policy annotations remain transport independent.

See the [service migration guide](../../docs/en/design/application-requests.md),
[configuration REST implementation](../../docs/en/design/configuration-rest.md), and
[Result migration](../../docs/en/design/result-unions.md).

## Installation

```shell
dotnet add package NOF.Contract
```

## License

Apache-2.0
