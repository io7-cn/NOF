# Result unions in NOF 12

[简体中文](../../zh-CN/design/result-unions.md) · [Development guide](../README.md)

NOF 12 uses C# 15 native unions on .NET 11. This is a source, binary and wire
breaking change from NOF 11. Rebuild contracts, servers and clients together.
An existing NOF 11 client cannot consume the NOF 12 JSON envelope.

## Model

```csharp
public union Result(Success, Failure) : IRpcResponse<Result>;
public union Result<T>(Success<T>, Failure) : IRpcResponse<Result<T>>;
```

`Success` contains `Message`; `Success<T>` contains non-null `Data` and `Message`.
Both messages default to an empty string and may contain successful operation
information. `Failure` contains a `ResultError` with a nonempty `Code` and a
non-null `Message`. Domain failure descriptors remain `NOF.Domain.Failure`;
transport failures use `NOF.Contract.Failure`.

```csharp
Result saved = new Success("Saved");
Result<OrderDto> loaded = new Success<OrderDto>(order, "Loaded from cache");
Result<OrderDto> missing = new Failure("order.not_found", "Order not found");

string Describe(Result<OrderDto> outcome) => outcome switch
{
    Success<OrderDto> success => $"{success.Data.Id}: {success.Message}",
    Failure failure => $"{failure.Code}: {failure.Message}",
    null => throw new InvalidOperationException("Uninitialized result")
};
```

`IsSuccess`, `ErrorCode`, `Extra`, `IResult`, `FailResult`, `ResultProjection`,
`Result.From`, and the old payload-bearing `Result<T>.Value` API are removed.
The native union still exposes its compiler-generated `Value`: that is the
selected **case object**, not successful data. Match a case and use `success.Data`.
The default union contains no case; RPC boundaries reject it.

Construction helpers remain: `Result.Success()`, `Result.Success(data, message)`,
`Result.Fail(code, message)`, `Result.Page(items, totalCount, message)` and
`Result.Stream(items, message)`. For string payloads use
`Result.Success<string>(data, message)` or `new Success<string>(data, message)`;
`Result.Success("Saved")` creates a payload-free success message.

## Pagination and streaming

Replace `PaginatedResult<T>` with `Result<Page<T>>`. The success case's data has
`Items` and optional `TotalCount`; these fields are typed rather than stored in
`Extra`. Replace `StreamingResult<T>` with `Result<IAsyncEnumerable<T>>`.
Generated clients return `Task<Result<IAsyncEnumerable<T>>>`.

```csharp
var outcome = await client.WatchAsync(request, Context.Empty, cancellationToken);
switch (outcome)
{
    case Success<IAsyncEnumerable<OrderEvent>> success:
        Console.WriteLine(success.Message);
        await foreach (var item in success.Data.WithCancellation(cancellationToken))
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

An initial stream failure uses the ordinary failure JSON. A successful HTTP or
JSON-RPC stream uses SSE for items and `X-NOF-Result-Message` for the successful
message (Base64-encoded UTF-8). Clients decode the header before returning the
success case. Cross-origin browser applications must expose this header in their
CORS policy using `WithExposedHeaders(StreamingResponseMetadata.MessageHeader)`.
Exceptions during enumeration remain stream failures; a success case does not
promise that an entire stream has completed successfully. JSON-RPC sends an error
event for a server-side enumeration exception.

## JSON contract

```json
{"kind":"success","data":{"id":42},"message":"Loaded"}
{"kind":"success","message":"Saved"}
{"kind":"failure","error":{"code":"order.not_found","message":"Missing"}}
```

`kind` is required and validated; typed success requires non-null `data`.
Contradictory cases, unknown case fields, and the old `isSuccess/value/extra`
envelope are rejected. Failure carries no success payload. The same failure JSON
can be read as `Result` or `Result<T>`. Source-generated System.Text.Json metadata
supports the union and case records; register concrete result/payload types in
the application's JSON context for Native AOT.

## RPC boundaries and migration

`IRpcResponse<TSelf>` extends the runtime `IUnion` interface and provides static
`CreateFailure` and `CreateEmptyResponse` factories for generated transports.
It contains no success flag, payload accessor or reflection-based conversion.
Custom response unions must implement this self-typed contract. A middleware
failure can be carried by any response union through `CreateFailure`;
`RpcResponse.Require<TResponse>` otherwise requires the exact declared response
type and a populated case. It does not project arbitrary successful objects.
A successful empty HTTP body creates `Result.Success()` only for a payload-free
response; a typed response with no data is rejected. Tagged failure bodies on
non-success HTTP status codes preserve the business error code and message;
other bodies become diagnostic text with the numeric HTTP status as the code.
OAuth endpoint bodies continue to follow the OAuth protocol rather than the
NOF RPC envelope.

Migrate consumers by matching each success/failure case, replace payload reads
with `success.Data`, replace failure reads with `failure.Code/Message`, and move
application metadata from `Extra` into explicit DTO properties. Deploy a coordinated
major-version upgrade or a separately versioned API route. To roll back, restore
the complete NOF 11 contract/client/server set and its original wire format.

See the [C# union reference](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
and [System.Text.Json union documentation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types).
