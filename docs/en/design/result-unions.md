# Operation-specific result unions in NOF 12

[简体中文](../../zh-CN/design/result-unions.md) · [Declared failures](expected-failures.md)

NOF 12 uses native C# 15 unions on .NET 11. Contract no longer provides generic
Result or Result<T>, their factories, or ResultJsonTypeClassifier. Success and
Success<T> wrappers are also removed. Upgrade contracts, implementations and callers
together; no compatibility aliases are shipped.

Declare the successful business value and expected failures on a partial service:

```csharp
public interface IOrderService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    OrderDto Get(GetOrderRequest request);
}

public partial class OrderService : ApplicationService<IOrderService>;
public sealed class GetOrder : OrderService.Get
{
    public override Task<Result> HandleAsync(
        GetOrderRequest request, InvocationContext context, CancellationToken ct)
        => Task.FromResult<Result>(new NotFound("Order not found"));
}
```

Here Result is the inherited IOrderService.GetOperation.Result, not a shared generic
result. It contains OrderDto and the operation's own NotFound case. Handlers directly
return DTOs, Empty.Instance or generated failure cases. Page<T> and IAsyncEnumerable<T>
are ordinary business values. Successful messages belong to business DTOs.

For operations without declared failures, return the DTO, nullable value, Empty or
an application-owned union directly. Protocol service interfaces outside the
IApplicationService model can declare concrete native unions in their owning package;
OAuth token, client and device services do this. There is no generic replacement
Result in another framework layer. The sample UI owns its separate UiResponse port.
OAuth application contracts use FailureCase just like other application services.

C# checks native union switch expressions; NOF adds no expected-outcome marker or
checked-result analyzer. Generated Match remains an optional convenience. Default
union contents remain invalid at the invocation boundary.

Generated failure JSON uses the branch Name in $failure and carries errorCode/message
or custom error data. Native HTTP adapters explicitly map results to resources and
ProblemDetails; there is no mandatory wire envelope. Consumers own JSON metadata.
See the declared-failure guide for source generation and AOT constraints.

DictionaryExtensions.CreateOrCopy and ContextExtensions.CopyHeadersFrom/ReplaceHeadersFrom
are deleted. Construct dictionaries directly. Use Context.WithItem/WithItems for
execution data; inbound adapters decode transported headers at the transport boundary.
Header case handling and tenant isolation remain covered by integration tests.
