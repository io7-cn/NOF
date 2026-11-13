# Declared expected failures

[简体中文](../../zh-CN/design/expected-failures.md) · [Application services](application-requests.md) · [Unified operations](unified-operations.md)

Contract declares every predictable business failure on its interface methods. Application generates the concrete Result union and failure branch types inside its Service.Operation. Contract interfaces do not need partial and receive no generated nested types.

```csharp
// Contract: declarations only, including the successful output type.
public sealed record DeleteConflict(long NodeId);
public interface INodeService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    [FailureCase("HasChildren", typeof(DeleteConflict))]
    Empty Delete(DeleteNodeRequest request);
}

// Application: no extra operation declaration or failure holder is required.
public partial class NodeService : ApplicationService<INodeService>;
public sealed class DeleteNode : NodeService.Delete
{
    public override Task<Result> HandleAsync(
        DeleteNodeRequest request, InvocationContext invocation, CancellationToken ct)
        => Task.FromResult<Result>(new NotFound("Node not found"));
}
```

The generated NodeService.Delete owns Result, NotFound and HasChildren. It directly inherits `ServiceOperation<DeleteNodeRequest, NodeService.Delete.Result>`. Handlers inherit these types and use `Task<Result>` and new NotFound(...). A separate ApplicationService implementation generates its own concrete result types; Contract contains only the shared declarations and data types.

## Names, codes and custom data

```csharp
[FailureCase("NotFound")]               // ErrorCode defaults to NotFound.
[FailureCase("NotFound", "000404")]     // Opaque error code, independent of the type name.
[FailureCase("HasChildren", typeof(DeleteConflict))] // Typed Error data without an imposed code.
```

The required Name is a unique valid C# identifier for a branch. The optional second argument is either a nonblank opaque code or typeof(CustomError). Constructor overloads keep those choices exclusive. Contract's FailureCaseAnalyzer reports NOF220 for invalid names, keywords, collisions, duplicate names and invalid declarations. Codes may be numeric or otherwise unsuitable as identifiers; different branches may share a code. Declare attributes on IApplicationService interface methods, not implementation methods.

Message cases expose ErrorCode and Message, with an optional message constructor argument. Custom cases expose Error and require the typed error value; they impose no ErrorCode or Message. Each declared name is a distinct sealed branch, even when multiple names share a data type. No open shared Failure branch, factory methods or empty failure-holder class is generated.

Return the successful value or a declared branch directly. Unrelated errors cannot implicitly convert to the closed union. Successful messages belong to business DTOs. FailureCase operations require one request and a synchronous non-nullable success type on a top-level non-generic contract; contracts without FailureCase retain nullable responses.

## References and execution descriptors

Contract generates no code. NOF.Contract.SourceGenerator retains its name and declaration analyzers. Application owns operation identity metadata and complete descriptors. Remote production uses concrete clients or generic IOutbox intents, independently of service descriptors.

Application generates `NodeService.Delete.Descriptor` as `ServiceOperationDescriptor<DeleteNodeRequest, NodeService.Delete.Result>`. HTTP invocation, local invocation and operation mappings use this complete response type. NodeService.Delete.InvokeAsync returns that same Result. The typed descriptor's Reference property exposes its input-only producer reference; both forms share the same stable Id, not the same object instance.

Message consumers still bind only complete Empty-output descriptors. Queue/Subscription reject Empty success methods with FailureCase, including forged success-only descriptors. Outbox messages are independent intents; infrastructure handlers explicitly interpret any application result they choose to invoke.

## Handling and layer boundaries

Use native C# union switch expressions for exhaustiveness checking (CS8509), or the generated Match helper. Ordinary C# discard, catch-all and switch-statement behavior remains unchanged. NOF adds no checked-result analyzer or ExpectedOutcome marker. Default/uninitialized unions are invalid at the invocation boundary.

Domain returns its own business values without referencing Contract. Application maps them to the declared operation branches. Technical exceptions and cancellation propagate; standard .NET exceptions represent invariant misuse. Shared Abstraction.Failure and ServiceRejected represent execution-policy rejection separately from a completed business result.

HTTP modules explicitly map application cases to TypedResults/resources/ProblemDetails. The configuration-node CRUD and OAuth sample adapters use Application-owned cases. Result is an in-process outcome, not a mandatory wire envelope.

## JSON and Native AOT

Generated results retain reflection-free classifiers. Optional union JSON uses the reserved $failure Name discriminator, together with errorCode/message or custom error data. Unknown branch names, mismatched markers and codes are rejected. Successful values serialize directly.

When explicitly serializing generated unions, compile the owning Application assembly first and register `Service.Operation.Result` in the consuming host's JsonSerializerContext. System.Text.Json cannot rely on another generator's output being visible during the same compilation. Native endpoint modules may instead map the result to already declared DTO/ProblemDetails types. The smoke test verifies a separately compiled Application result with JSON reflection disabled.

## Breaking migration

Keep FailureCaseAttribute on Contract methods and remove partial from service interfaces. Replace `IService.<Operation>Operation.Result` and its branch references with `Service.Operation.Result` and Service.Operation.Case. No Contract operation base or compatibility aliases remain. Handlers that already use inherited `Task<Result>` and new Case(...) keep that source shape.

Use complete Application descriptors for HTTP/local execution and inbound bindings. Immediate clients and generic Outbox intents do not route by an operation reference. Do not treat the method's successful return type as the complete execution output.

Keep JSON branch names/codes and custom data schemas stable when migrating. The CLR type ownership changes; the optional existing JSON discriminator format does not. Upgrade application, endpoints, JSON contexts and in-process callers together. Register source-generated JSON metadata from the new owning Application assembly when used. Both monolithic generator orders and a separately compiled metadata-only Contract are covered by tests.

[Current Contract/Application/Abstraction boundaries](contract-boundaries.md).

See [generic transactional outbox](transactional-outbox.md).
