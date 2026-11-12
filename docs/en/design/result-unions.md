# Result unions in NOF 12

[简体中文](../../zh-CN/design/result-unions.md) · [Development guide](../README.md)

NOF 12 uses native C# 15 unions on .NET 11. This is a source, binary and wire
breaking change from NOF 11. Rebuild contracts, servers and clients together.

## Values are the success cases

```csharp
public union Result<T>(T, Failure);
public union Result(Empty, Failure);
```

`Result<T>` contains `T` directly. Payload-free `Result` uses the existing `Empty`
type. `Success` and `Success<T>` are removed; successful messages belong to explicit
business response DTOs. No replacement generic message wrapper is provided.

```csharp
Result<OrderDto> loaded = order;
Result<ImportReceipt> imported = new ImportReceipt(42, "Import completed");
Result<OrderDto> missing = new Failure("order.not_found", "Order not found");
Result saved = Empty.Instance;

string Describe(Result<OrderDto> outcome) => outcome switch
{
    OrderDto value => $"Order {value.Id}",
    Failure failure => $"{failure.Code}: {failure.Message}",
    null => throw new InvalidOperationException("Uninitialized result")
};

public sealed record ImportReceipt(int ImportedCount, string Message);
```

Match the payload itself. There is no `IsSuccess`, `Extra`, `IResult`, `FailResult`,
`ResultProjection` or `Result.From`. The compiler-generated union `Value` contains
the selected value/case as an object; use typed patterns for application code.
A default/null union has no selected value and is rejected at application boundaries.

Construction helpers are `Result.Success(data)`, `Result.Fail(code, message)`,
`Result.Page(items, totalCount)` and `Result.Stream(items)`.
`Result.Success()` returns the explicit Empty case. `Result.Success("Saved")` now
creates `Result<string>` containing the string itself. Remove calls to WithMessage
and declare a business DTO for successful message data. A default Result remains
uninitialized and must not be treated as success.

## One shared failure

`NOF.Abstraction.Failure` is the only shared failure value. It has a nonblank
`Code` and non-null `Message`; its constructor order is `(code, message)`.
There are no parallel Contract or Domain definitions and no conversion adapter.
A generated domain failure can be returned directly from any `Result<T>` handler.
`Result.Fail(existingFailure)` returns that same shared instance.

The `[Failure(name, message, errorCode)]` annotation remains in Domain; its generator
now emits `NOF.Abstraction.Failure(code, message)`. Domain exceptions accept that
shared value. Import `NOF.Domain` to use `failure.Throw()`: it is a Domain extension
that throws `DomainException`, keeping Abstraction independent of Domain.

## JSON and AOT

System.Text.Json writes the active value directly:

```json
{"id":42}
{"importedCount":42,"message":"Import completed"}
{}
{"$kind":"failure","code":"order.not_found","message":"Missing"}
```

These represent `Result<OrderDto>`, `Result<ImportReceipt>`, payload-free
`Result`, and a shared failure respectively. A custom native union classifier
selects failures by the reserved root `$kind: "failure"` marker; it does not infer
failure from `code` and `message` fields. Scalar, DTO, collection and dictionary
payloads are supported, including successful DTOs with error-like property names.

The root `$kind` property is reserved by the Result protocol. If successful data
needs this property, define a business DTO that nests the data under another field.
Empty accepts only an empty JSON object; old message-only successes are rejected.
Invalid markers, malformed failures and mixed failure/payload fields are rejected.
The old `isSuccess/value/extra` envelope is not a supported compatibility format.

The classifier uses source-generated union case metadata without constructing
closed generic converters through reflection. Register concrete result and payload
types in an application's JSON context for Native AOT; shared Failure metadata is
also included in NOF's built-in context.

## Pagination and streaming

Replace `PaginatedResult<T>` with `Result<Page<T>>`; successful `Page<T>` has typed
`Items` and optional `TotalCount`. Replace `StreamingResult<T>` with
`Result<IAsyncEnumerable<T>>`. Stream messages belong to event DTOs or explicit
application-defined events. These streams support server SSE endpoints and corresponding
`Task<Result<...>>` server invocation return types. HTTP callers maintain their own
SSE reader; no client is generated.

```csharp
var result = await OrderService.Watch.InvokeAsync(services, request, Context.Empty, cancellationToken);
switch (result)
{
    case IAsyncEnumerable<OrderEvent> events:
        await foreach (var item in events.WithCancellation(cancellationToken))
        {
            Console.WriteLine(item);
        }
        break;
    case Failure failure:
        Console.WriteLine(failure.Message);
        break;
    case null:
        throw new InvalidOperationException("Uninitialized result");
}
```

The endpoint module maps a pre-stream failure to ProblemDetails and a successful stream
to native TypedResults.ServerSentEvents. Callers use the platform SseParser. There is no
NOF streaming response envelope or message header. If a stream needs message metadata,
model it explicitly in event DTOs or an application-defined event. Each adapter
decides how to represent successful business message data. Exceptions during
enumeration remain stream failures; initial success does not promise stream completion.

## Boundaries, deployment and rollback

`IRpcResponse<TSelf>` and its empty-response factory are removed. Result and custom
business unions carry no RPC outcome interface. Generated service invocation reports
policy rejection with ServiceRejectedException; REST adapters map it independently of
business Failure cases. Legacy HTTP RPC adapters, annotations and protocol readers
are deleted without a compatibility layer. HTTP routing belongs to endpoint modules.
Native REST endpoints return resource DTOs, 201/204 or ProblemDetails rather than a
mandatory Result envelope. OAuth endpoints retain their standard protocol.

Match successful `T` directly, use Empty for payload-free success and business DTOs
for successful messages, replace failure
reads with `failure.Code/Message`, and move `Extra` metadata into explicit DTOs.
Deploy a coordinated major-version upgrade or separately versioned routes. Rollback
restores the complete NOF 11 client/contract/server set and its original wire format.

See the [C# union reference](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
and [System.Text.Json union documentation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types).
