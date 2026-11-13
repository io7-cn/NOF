# Contract declarations and application execution

[简体中文](../../zh-CN/design/contract-boundaries.md) · [Expected failures](expected-failures.md) · [Unified operations](unified-operations.md)

NOF.Contract contains declarations and data models only. Its runtime assembly references no other NOF assembly, exposes no forwarded runtime types and needs no Application dependency. Contract's analyzers still validate service signatures and FailureCase declarations and generate input-only operation references.

| Owner | Types and responsibilities |
|---|---|
| Contract | IApplicationService, FailureCase/policy metadata attributes, requests, successful DTOs, custom error data, Empty/Optional/Page and declarative ServiceOperationReference |
| Application | ServiceOperation, InvocationContext/InvocationOrigin, full ServiceOperationDescriptor, ServiceExecution/ServiceCompleted/ServiceRejected/RejectionReason/ServiceRejectedException, invocation interfaces and metadata interpretation |
| Abstraction | Context and its immutable semantic items/async-flow binding, shared Failure and other foundation services |
| Host/infrastructure | Transport adaptation, identity establishment, selected invocation engine, HTTP presentation, queue routing and durable receipt/retry behavior |

## Declaring versus executing

```csharp
// Contract
public interface INodeService : IApplicationService
{
    [FailureCase("Missing", "404")]
    NodeDto Read(ReadNode request);
}

// Application
public partial class NodeService : ApplicationService<INodeService>;
public sealed class ReadNodeHandler : NodeService.Read
{
    public override Task<Result> HandleAsync(
        ReadNode request, InvocationContext invocation, CancellationToken ct)
        => Task.FromResult<Result>(new Missing("Node not found"));
}
```

FailureCase stays on the non-partial Contract interface method; concrete Result and branch types remain generated in the Application operation. Runtime execution types now use namespace NOF.Application. Semantic Context uses namespace NOF.Abstraction as well as its existing Abstraction assembly.

INodeServiceOperations.Read is pure declaration data: contract type, operation name, stable Id and input type. It neither resolves MethodInfo nor validates method existence/input shape or decides CanSubmit. Constructing a reference does not execute or inspect an operation. Application interprets it when it crosses an execution or submission boundary.

NodeService.Read.Descriptor supplies the complete response type and resolved method metadata. Its Reference property returns the declarative producer form. Validation is centralized in ServiceOperationInspector.ValidateReference; CanSubmit checks for an Empty return with no declared FailureCase, and RequiresHostTenant interprets equivalent policy metadata. Default bindings and dispatch routes invoke these checks. Moving the logic does not bypass authorization or permit discarded business failures.

## Migration

- Import NOF.Application for ServiceOperation, InvocationContext, descriptors, execution outcomes and rejection types. No Contract aliases/forwarders are retained.
- Import NOF.Abstraction for Context and replace explicit NOF.Contract.Context references. Its semantic behavior and ambient async-flow restoration stay the same. The obsolete Contract TypeForwardedTo entry is removed intentionally.
- Replace reference.MethodInfo/CanSubmit with Application-owned ServiceOperationInspector calls where direct inspection is needed. Normal application invocation and message binding APIs already validate; business handlers do not need to repeat it.
- Replace UseHostTenantAttribute.IsRequired(method) with ServiceOperationInspector.RequiresHostTenant(method). The annotation itself remains in Contract.
- Keep clients dependent on Contract and foundation services only. The sample HTTP client uses HttpRequestException/status codes for transport errors, instead of importing Application's execution rejection exception.

Update runtime, providers, hosts and source-generator packages together. Contract DTOs and failure declarations, generated Application branch names and optional JSON union discriminators remain unchanged. No database or stored message-route migration is needed for this layer/namespace change.

Boundary tests verify that Contract depends on no NOF runtime assembly, has no forwarded execution types or reflective reference members, and that interpretation, execution models and semantic Context have their intended owners. Existing execution, authorization, messaging, HTTP and source-generator isolation regressions remain in the suite.
