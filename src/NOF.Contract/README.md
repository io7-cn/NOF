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

Use FailureCase on partial service methods to declare expected failures. The method
return is the successful type; generated IService.OperationResult includes one distinct
case per declared code. Codes are opaque strings and case names are independent.
See [expected failures](../../docs/en/design/expected-failures.md).

`Result<T>` is native `union (T, NOF.Abstraction.Failure)`. Match successful T directly;
payload-free Result uses Empty. Success/Success<T> and WithMessage are removed; successful
messages belong to business response DTOs. Result has
no IRpcResponse interface or empty-response factory. Policy rejection belongs to the
execution outcome, separately from returned business failures.

Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint, HTTP verbs, transport annotations and
protocol readers are deleted. Contracts need no Hosting reference. Native HTTP modules
own routing, binding and status mappings. Context remains explicit execution metadata;
application permission and host-tenant policy annotations remain transport independent.

See the [service migration guide](../../docs/en/design/application-requests.md),
[configuration REST implementation](../../docs/en/design/configuration-rest.md), and
[Result migration](../../docs/en/design/result-unions.md).

## Installation

```shell
dotnet add package NOF.Contract
```

## License

Apache-2.0
