# NOF.Contract

Transport-independent declarations and data models. The assembly references only NOF.Abstraction among NOF runtime packages, sharing foundational permission models.

```csharp
public interface IOrderService : IApplicationService
{
    [FailureCase("Missing", "404")]
    OrderDto Get(GetOrderRequest request);
}
```

Declare one reference-type input and a synchronous business success type, or void for a one-way operation. Void has no business reply or FailureCase; Empty explicitly retains a reply, including declared failures. Interfaces do not need partial. FailureCase/policy annotations remain here; Application generates Result and failure types in Service.Operation. NOF.Contract.SourceGenerator retains its name and declaration analyzers; Contract generates no code or operation references.

Application owns the complete OperationDescriptor, which validates contract identity and input without constructing a handler. Input-only ServiceOperationReference and Descriptor.Reference are removed; submission eligibility and host-tenant policy interpretation remain Application concerns. Complete execution descriptors, OperationHandler, InvocationContext, execution outcomes and rejection exceptions also belong to Application. Context belongs to Abstraction, with no Contract forwarding facade.

This package retains IApplicationService, declaration attributes, Empty/Optional/Page and ordinary request/response/error data models. HTTP adapters, generated clients, generic result wrappers and execution machinery are not included. Native endpoints own resource/ProblemDetails mappings; clients own HTTP integration.

See [layer boundaries](../../docs/en/design/contract-boundaries.md) and [expected failures](../../docs/en/design/expected-failures.md), each with a Chinese edition.

Permissions belong exclusively to Contract interfaces and operation methods: RequireAuthenticated, nonempty RequirePermission groups with PermissionMatch.All/Any, and AllowAnonymous. NOF240/241/242 validate built-in declarations. MetadataAttribute is removed; SummaryAttribute directly inherits Attribute. Application resolves Contract policies; Abstraction supplies shared permission models and checks; handlers and UI pages are not permission declaration sources. See [authorization](../../docs/en/design/authorization.md) ([简体中文](../../docs/zh-CN/design/authorization.md)).

## Installation

```shell
dotnet add package NOF.Contract
```

## License

Apache-2.0
