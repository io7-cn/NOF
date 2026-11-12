# NOF.Contract

Transport-independent application service declarations, messages and business outcomes.

```csharp
public interface IOrderService : IApplicationService
{
    OrderDto Get(GetOrderRequest request);
}
```

Declare one reference-type request and a non-Task business return. Do not declare
Context/CancellationToken/Async on the proto interface. Generated server operations
receive those execution arguments. DTOs, nullable values and business unions work
without response marker interfaces. No client code is generated.

Use FailureCase on partial service methods to declare expected failures. The method
return is the successful type; generated IService.<Operation>Operation.Result includes one distinct
case per required name. Use FailureCase("NotFound"), FailureCase("NotFound", "404"),
or FailureCase("Conflict", typeof(MyError)). Message cases expose ErrorCode/Message;
custom cases expose typed Error without an imposed code. Invalid names report NOF220.
See [expected failures](../../docs/en/design/expected-failures.md).

Generic Result/Result<T>, their factories and JSON classifier are removed. Only generated
operation-specific Result unions remain. Return ordinary DTOs, nullable values, Empty or
business unions for unannotated operations. Success wrappers are removed; successful
messages belong to business response DTOs. Policy rejection remains separate.
DictionaryExtensions and ContextExtensions are removed; use normal dictionaries and
Context.WithItem/WithItems. Transport adapters own header decoding.

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

Generated Contract operation bases own nested Result and failure types. ServiceOperation
and its generic form also live in this package. Application-derived operations inherit
the canonical types so handlers use Task<Result> and new Case(...). No factories or
expected-outcome marker/analyzer are generated; native C# checks switch expressions.
