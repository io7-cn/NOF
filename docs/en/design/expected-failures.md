# Declared expected failures

[简体中文](../../zh-CN/design/expected-failures.md) · [HTTP adaptation](http-adaptation.md)

Contract declares predictable business failures on non-partial interface methods. Application generates the closed Result union and distinct sealed branch types inside Service.Operation; Contract contains declarations and data models only.

```csharp
public sealed record DeleteConflict(long NodeId);
public interface INodeService : IApplicationService
{
    [FailureCase("NotFound", "Node not found")]
    [FailureCase("HasChildren", "Node has children", typeof(DeleteConflict))]
    Empty Delete(DeleteNodeRequest request);
}
public partial class NodeService : ApplicationService<INodeService>;
public sealed class DeleteNode : NodeService.Delete
{
    public override Task<Result> HandleAsync(
        DeleteNodeRequest request, InvocationContext context, CancellationToken ct)
        => Task.FromResult<Result>(new HasChildren(new DeleteConflict(request.Id), "Delete the children first."));
}
```

Name must be a unique valid C# branch identifier; NOF220 reports invalid declarations. Title is a nonblank display summary, independent of the identifier. `[FailureCase("NotFound")]` defaults Title to Name. A custom Error type is the third argument, after the summary. Summaries can contain spaces or numbers and can repeat across branches. Never classify a failure or choose an HTTP status by comparing Title.

All generated branches implement Abstraction.IFailure (Title, Detail); custom branches additionally implement IFailure<TError> and expose Error. Default constructors take optional detail; custom constructors take required error and optional detail. The shared Failure also exposes Title/Detail. No Code, ErrorCode or Message compatibility aliases, CreateCase factories, empty failure holders or open IFailure union branch are generated. Successful messages belong to DTOs.

Handlers inherit Task<Result> and new Case(...). A result only accepts its declared success and failure types. Native C# union switch expressions provide exhaustiveness checking; NOF adds no checked-result analyzer. Technical exceptions/cancellation propagate. Domain returns business values; Application maps them to declared cases without Domain depending on Contract.

Service.Operation.Descriptor carries the complete business Result. HandleAsync returns Task<Result>, while complete InvokeAsync returns Task<object?> because effective host filters may also return their own concrete failures. InvocationResult.Value unwraps the boxed business union. Default/uninitialized unions and undeclared failures are rejected at the invocation boundary. Plain nullable and native-union responses remain available on methods without FailureCase; native union case metadata is obtained from type declarations/constructors, never user control flow.

Application operation bases implement IServiceOperationDefinition through static Descriptor/Describe, enabling `group.MapDelete<NodeService.Delete>("/{id}")`. HTTP defaults declared IFailure cases to 422 ProblemDetails; configure 404/409 explicitly by concrete case type. The wire body is a native resource or ProblemDetails, with no Result envelope and no duplicate code/message extensions. See [HTTP adaptation](http-adaptation.md).

Partial filters can also declare FailureCase and generate static FailureCases catalogs, without a Filter.Result union. Effective invocation plans describe only enabled cases without constructing handlers or filters. See [invocation extensions](invocation-extensions.md).

Optional union JSON uses the reserved $failure Name discriminator, title/detail and optional error data. Register Application-owned Service.Operation.Result in the consuming host's JsonSerializerContext after compiling Application. JSON source generation cannot consume another generator's output in the same compilation. HTTP mapping can instead serialize declared DTO/ProblemDetails types. Reflection-disabled smoke tests cover both paths.

Migrate ErrorCode/Code to Title and Message to Detail, change custom attributes to `(name, title, typeof(Error))`, and update serializers/consumers together. Preserve distinct CLR cases for branching. OAuth client repositories and registration admission ports now return closed unions with OAuthClientNotFound/OAuthClientConflict/etc.; use type patterns. Remote adapters carry actual HttpStatusCode in HttpTransportFailure. Standard OAuth protocol error strings remain protocol data and are mapped explicitly at the HTTP boundary.
