# Declared expected failures

[简体中文](../../zh-CN/design/expected-failures.md) · [Application services](application-requests.md)

Declare predictable business failures on Contract methods with `FailureCase`. The
method's return type describes success. The Contract generator emits a closed
`IService.<Operation>Operation.Result` union, and the Application generator uses it consistently
for the operation override, mapping, Descriptor and InvokeAsync return type.

```csharp
public sealed record DeleteConflict(long NodeId);

public partial interface INodeService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    [FailureCase("HasChildren", typeof(DeleteConflict))]
    Empty Delete(DeleteNodeRequest request);
}
```

The first argument is always a required C# branch name. The optional second argument
is either an opaque nonblank error code or `typeof(CustomError)`:

```csharp
[FailureCase("NotFound")]               // ErrorCode defaults to "NotFound"
[FailureCase("NotFound", "404")]        // Explicit ErrorCode
[FailureCase("HasChildren", typeof(DeleteConflict))] // Custom data, no ErrorCode
```

Constructor overloads express these mutually exclusive choices because attribute
arguments cannot themselves be C# union values. There is no generic attribute or
settable Name. Invalid names, keywords, duplicate names and generated-member
collisions produce NOF220 compilation errors. Numeric codes are valid, but a name
such as "404", "not-found" or "class" is invalid. Codes need not be unique: the
required Name identifies each branch.

Message branches directly expose ErrorCode and Message and take an optional message
in their constructor. Custom branches expose typed Error data and take that value in
their constructor; no ErrorCode or Message is imposed on custom data. There is no
shared Error wrapper.

The generated declaration is conceptually:

```csharp
// Inside the generated partial INodeService declaration:
public abstract class DeleteOperation : ServiceOperation<DeleteNodeRequest, DeleteOperation.Result>
{
    public union Result(Empty, NotFound, HasChildren);
    // Generated NotFound and HasChildren records are nested here.
}
// Generated nested records carry Error data, message/code or custom Error data, plus JSON metadata.
```

Each declared name has its own case type. Two names carrying the same error-data type still
remain distinct branches. No open shared Failure case is appended. Handlers can return
Empty.Instance, a success DTO, or one of the declared cases; an unrelated failure cannot
implicitly convert to the result. There is no handwritten failure-holder class.

```csharp
public partial class NodeService : ApplicationService<INodeService>;

public sealed class DeleteNode : NodeService.Delete
{
    public override Task<Result> HandleAsync(
        DeleteNodeRequest request, Context context, CancellationToken ct)
        => Task.FromResult<Result>(request.Id <= 0
            ? new NotFound("Node not found")
            : new HasChildren(new DeleteConflict(request.Id)));
}
```

Application's generated NodeService.Delete inherits INodeService.DeleteOperation.
Handlers therefore use Task<Result> and directly construct new NotFound(...) or
new HasChildren(data). No factories, duplicate or derived error types are generated.
ServiceOperation and ServiceOperation<TRequest,TResponse> now live in NOF.Contract,
so the Contract operation base has no Application dependency. Update explicit imports
from NOF.Application.ServiceOperation to NOF.Contract.ServiceOperation.

Callers handle the generated union with an exhaustive switch expression or generated
Match method. Match requires one success callback and one callback per declared case;
named callback parameters use `onCase<Name>`. Adding a failure changes that signature.
Do not widen the result to object or a shared untyped error merely to avoid handling it.

Exhaustiveness checking belongs to the C# compiler: an incomplete union switch
expression produces CS8509. NOF adds no checked-result analyzer, ExpectedOutcome
marker, discard restriction or catch-all prohibition. Native C# still permits
ignoring a result and using a catch-all arm. Switch statements do not provide the
same exhaustiveness warning as switch expressions. Match also remains an optional
convenience requiring an explicit callback for each case. Null/default union values
remain invalid at the application invocation boundary.

## Layer and transport boundaries

The attribute, generated failure types and generator belong to Contract. Domain does
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
`$failure` branch Name, plus `errorCode`/`message` or custom `error` data. Unknown names,
mismatched codes and case markers are
rejected. Success serializes its business value. This is an optional codec, not an HTTP
response envelope. Native HTTP modules continue returning resources or ProblemDetails.

For source-generated JSON metadata, compile generated contract types in the Contract
assembly, then register their concrete outcome types in the consumer's JsonSerializerContext.
System.Text.Json's generator cannot consume another generator's output in the same
compilation. Monolithic native HTTP adapters can map the in-memory outcome to existing
DTO/ProblemDetails types without serializing the generated union itself. Tests cover both
generator orders in a monolith and real Native AOT with a separately compiled contract.

Migrate `[FailureCase("404", Name = "NotFound")]` to
`[FailureCase("NotFound", "404")]`; migrate generic error declarations to
`[FailureCase("Conflict", typeof(MyError))]`. Replace default `.Error.Message` with
`.Message` and static `.Code` with the instance `.ErrorCode`. Custom branches retain
`.Error` and no longer provide a generated business code. The JSON discriminator now
uses the branch Name instead of its old code; update serialized consumers together.

Update contract signatures, handler return types, callers and adapters together. Existing
unannotated DTO/nullable/Result methods retain their behavior. Supported declarations are
top-level, non-generic partial service interfaces, unique non-generic operations with one
request and a non-nullable synchronous success type. Use explicit data types for optional
success values. Runtime speed relative to returned Result values is not assumed; avoiding
exception-based control flow does not eliminate allocations or replace benchmarking.
