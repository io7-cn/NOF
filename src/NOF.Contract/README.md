# NOF.Contract

Transport-independent declarations and data models. The runtime assembly has no dependency on other NOF assemblies.

```csharp
public interface IOrderService : IApplicationService
{
    [FailureCase("Missing", "404")]
    OrderDto Get(GetOrderRequest request);
}
```

Declare one reference-type input and a synchronous business success type. Interfaces do not need partial. FailureCase/policy annotations remain here; Application generates Result and failure types in Service.Operation. NOF.Contract.SourceGenerator retains its name and declaration analyzers; Contract generates no code or operation references.

Application owns ServiceOperationReference, which contains contract identity, operation name and input type only. Reflection, method/input validation and submission eligibility belong to Application's ServiceOperationInspector. Complete execution descriptors, ServiceOperation, InvocationContext, execution outcomes and rejection exceptions also belong to Application. Context belongs to Abstraction, with no Contract forwarding facade.

This package retains IApplicationService, declaration attributes, Empty/Optional/Page and ordinary request/response/error data models. HTTP adapters, generated clients, generic result wrappers and execution machinery are not included. Native endpoints own resource/ProblemDetails mappings; clients own HTTP integration.

See [layer boundaries](../../docs/en/design/contract-boundaries.md) and [expected failures](../../docs/en/design/expected-failures.md), each with a Chinese edition.

## Installation

```shell
dotnet add package NOF.Contract
```

## License

Apache-2.0
