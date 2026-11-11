# Result unions in NOF 12

[简体中文](../../zh-CN/design/result-unions.md) · [Development guide](../README.md)

NOF 12 uses native C# 15 unions on .NET 11. This is a source, binary and wire
breaking change from NOF 11. Rebuild contracts, servers and clients together.

## Values are the success cases

```csharp
public union Result<T>(T, Failure) : IRpcResponse<Result<T>>;
public union Result(Success, Failure) : IRpcResponse<Result>;
```

`Result<T>` contains `T` directly. `Success<T>` is optional metadata containing
non-null `Data` and `Message`; select it explicitly as the payload type when a
successful response needs a message. Payload-free `Result` uses `Success`, whose
message defaults to an empty string. Ordinary DTOs can also define their own
message properties without using a framework wrapper.

```csharp
Result<OrderDto> loaded = order;
Result<Success<OrderDto>> withMessage = new Success<OrderDto>(order, "Loaded from cache");
Result<OrderDto> missing = new Failure("order.not_found", "Order not found");
Result saved = new Success("Saved");

string Describe(Result<OrderDto> outcome) => outcome switch
{
    OrderDto value => $"Order {value.Id}",
    Failure failure => $"{failure.Code}: {failure.Message}",
    null => throw new InvalidOperationException("Uninitialized result")
};
```

Match the payload itself. There is no `IsSuccess`, `Extra`, `IResult`, `FailResult`,
`ResultProjection` or `Result.From`. The compiler-generated union `Value` contains
the selected value/case as an object; use typed patterns for application code.
A default/null union has no selected value and is rejected at RPC boundaries.

Construction helpers are `Result.Success(data)`, `Result.Fail(code, message)`,
`Result.Page(items, totalCount)` and `Result.Stream(items)`.
`Result.WithMessage(data, message)` returns `Result<Success<T>>`.
Use `Result.Success<string>(text)` for a string payload;
`Result.Success("Saved")` creates a payload-free successful message.

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
{"data":{"id":42},"message":"Loaded from cache"}
{"message":"Saved"}
{"$kind":"failure","code":"order.not_found","message":"Missing"}
```

These represent `Result<OrderDto>`, `Result<Success<OrderDto>>`, payload-free
`Result`, and a shared failure respectively. A custom native union classifier
selects failures by the reserved root `$kind: "failure"` marker; it does not infer
failure from `code` and `message` fields. Scalar, DTO, collection and dictionary
payloads are supported, including successful DTOs with error-like property names.

The root `$kind` property is reserved by the Result protocol. If successful data
needs this property, select `Result<Success<T>>` so it appears inside `data`.
Invalid markers, malformed failures and mixed failure/payload fields are rejected.
The old `isSuccess/value/extra` envelope is not a supported compatibility format.

The classifier uses source-generated union case metadata without constructing
closed generic converters through reflection. Register concrete result and payload
types in an application's JSON context for Native AOT; shared Failure metadata is
also included in NOF's built-in context.

## Pagination and streaming

Replace `PaginatedResult<T>` with `Result<Page<T>>`; successful `Page<T>` has typed
`Items` and optional `TotalCount`. Replace `StreamingResult<T>` with
`Result<IAsyncEnumerable<T>>`. If a stream needs a successful message, declare
`Result<Success<IAsyncEnumerable<T>>>`. Both forms generate streaming clients and
SSE endpoints, with corresponding `Task<Result<...>>` client return types.

```csharp
var result = await client.WatchAsync(request, Context.Empty, cancellationToken);
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

A pre-stream failure uses ordinary Failure JSON. An explicitly wrapped successful
stream sends its message in `X-NOF-Result-Message` (Base64 UTF-8), which generated
clients decode into `Success<IAsyncEnumerable<T>>`. Cross-origin browser applications
must expose that header using `WithExposedHeaders(StreamingResponseMetadata.MessageHeader)`.
Bare streams remain bare. Exceptions during enumeration remain stream failures;
an initial successful value does not promise successful completion of the stream.

## Boundaries, deployment and rollback

`IRpcResponse<TSelf>` extends `IUnion` and supplies static `CreateFailure` and
`CreateEmptyResponse` factories. It has no success flag or reflective payload
projection. Custom response unions use the shared Abstraction failure.
`RpcResponse.Require<TResponse>` preserves an exact declared response type or
propagates a shared failure through its static factory; it rejects an absent case
or a different successful response type. An empty successful HTTP body is valid
for payload-free Result only. Marked failure bodies on non-success HTTP statuses
preserve the business code and message; other bodies become diagnostics with the
numeric HTTP status as the code. OAuth endpoints retain their standard protocol.

Match successful `T` directly, opt into `Success<T>` for messages, replace failure
reads with `failure.Code/Message`, and move `Extra` metadata into explicit DTOs.
Deploy a coordinated major-version upgrade or separately versioned routes. Rollback
restores the complete NOF 11 client/contract/server set and its original wire format.

See the [C# union reference](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
and [System.Text.Json union documentation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types).
