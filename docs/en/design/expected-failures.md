# Declared expected failures

[简体中文](../../zh-CN/design/expected-failures.md) · [Application services](application-requests.md)

Declare predictable business failures on Contract methods with `FailureCase`. The
method's return type describes success. The Contract generator emits a closed
`IService.OperationResult` union, and the Application generator uses it consistently
for the operation override, mapping, Descriptor and InvokeAsync return type.

```csharp
public sealed record DeleteConflict(long NodeId);

public partial interface INodeService : IApplicationService
{
    [FailureCase("404", Name = "NotFound")]
    [FailureCase<DeleteConflict>("10001/?", Name = "HasChildren")]
    Empty Delete(DeleteNodeRequest request);
}
```

Error codes are opaque, nonblank strings. Numeric and non-identifier codes are valid
and preserved exactly. C# case names are independent `Name` arguments. An operation
with multiple failures requires a unique explicit Name for each declaration. A single
unnamed declaration, such as `[FailureCase("404")]`, always generates `Failed`, without
deriving an identifier from the code. The non-generic attribute uses built-in
`NOF.Contract.Error`, containing Message. The generic attribute selects error data;
it does not select the generated branch's name.

The generated declaration is conceptually:

```csharp
// Inside the generated partial INodeService declaration:
public union DeleteResult(Empty, DeleteResult.NotFound, DeleteResult.HasChildren);
// Generated nested records carry Error data, a stable Code constant and JSON metadata.
```

Each code has its own case type. Two codes carrying the same error-data type still
remain distinct branches. No open shared Failure case is appended. Handlers can return
Empty.Instance, a success DTO, or one of the declared cases; an unrelated failure cannot
implicitly convert to the result. There is no handwritten failure-holder class.

```csharp
public partial class NodeService : ApplicationService<INodeService>;

public sealed class DeleteNode : NodeService.Delete
{
    public override Task<INodeService.DeleteResult> HandleAsync(
        DeleteNodeRequest request, Context context, CancellationToken ct)
        => Task.FromResult<INodeService.DeleteResult>(
            new INodeService.DeleteResult.HasChildren(new DeleteConflict(request.Id)));
}
```

Callers handle the generated union with an exhaustive switch expression or generated
Match method. Match requires one success callback and one callback per declared case;
named callback parameters use `onCase<Name>`. Adding a failure changes that signature.
Do not widen the result to object or a shared untyped error merely to avoid handling it.

The Contract analyzer reports errors for directly discarded outcomes/tasks (`NOF230`),
incomplete switch expressions or switch statements (`NOF231`), and catch-all switch arms
that hide future cases (`NOF232`). It is not a language-level effect system or full
data-flow proof: C# still permits deliberate type erasure, storage without consumption,
and diagnostic suppression. Forwarding the typed outcome is valid. Null/default union
values remain invalid at the application invocation boundary.

## Layer and transport boundaries

The attribute, Error data type, generator and analyzer belong to Contract. Domain does
not depend on Contract. Domain rules return their own business values; Application
maps them to the declared use-case cases. Known input failures should be checked before
calling throwing invariant constructors. Domain exceptions remain for violated
invariants/programmer misuse; technical exceptions and cancellation propagate normally.

The former Domain FailureAttribute, static-holder generator, failure.Throw extension
and exception constructors accepting Failure are removed. Shared Abstraction.Failure
still represents policy rejection and supports existing open Result APIs; it is not an
implicit escape branch in a generated checked outcome. Authentication/tenant rejection
continues through ServiceRejectedException, independently of declared business failures.

HTTP modules explicitly translate each case to native TypedResults/ProblemDetails. The
configuration-node sample uses Match for node CRUD, files, configuration and streams;
the app-owned UI adapter consciously projects those cases to its simpler UI port.

## JSON, AOT and migration

Generated outcomes have reflection-free classifiers. Failure JSON carries the reserved
`$failure` code and typed `error` data; unknown codes and mismatched case markers are
rejected. Success serializes its business value. This is an optional codec, not an HTTP
response envelope. Native HTTP modules continue returning resources or ProblemDetails.

For source-generated JSON metadata, compile generated contract types in the Contract
assembly, then register their concrete outcome types in the consumer's JsonSerializerContext.
System.Text.Json's generator cannot consume another generator's output in the same
compilation. Monolithic native HTTP adapters can map the in-memory outcome to existing
DTO/ProblemDetails types without serializing the generated union itself. Tests cover both
generator orders in a monolith and real Native AOT with a separately compiled contract.

Update contract signatures, handler return types, callers and adapters together. Existing
unannotated DTO/nullable/Result methods retain their behavior. Supported declarations are
top-level, non-generic partial service interfaces, unique non-generic operations with one
request and a non-nullable synchronous success type. Use explicit data types for optional
success values. Runtime speed relative to returned Result values is not assumed; avoiding
exception-based control flow does not eliminate allocations or replace benchmarking.
