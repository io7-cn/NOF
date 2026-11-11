# NOF 12 的 Result 联合类型

[English](../../en/design/result-unions.md) · [开发指南](../README.md)

NOF 12 在 .NET 11 上使用 C# 15 原生联合类型。这次更新破坏源码、二进制和
JSON 协议兼容性，必须一起重新构建契约、服务端和客户端。NOF 11 客户端不能
读取 NOF 12 的新结果信封。

## 类型模型

```csharp
public union Result(Success, Failure) : IRpcResponse<Result>;
public union Result<T>(Success<T>, Failure) : IRpcResponse<Result<T>>;
```

`Success` 包含 `Message`，`Success<T>` 包含非空的 `Data` 和 `Message`。
成功消息默认为空字符串，也可以描述操作成功后的信息。`Failure` 包含
`ResultError`，其中 `Code` 不能为空或空白，`Message` 不能为 null。
领域失败描述符仍是 `NOF.Domain.Failure`，传输失败分支是 `NOF.Contract.Failure`。

```csharp
Result saved = new Success("保存成功");
Result<OrderDto> loaded = new Success<OrderDto>(order, "从缓存加载成功");
Result<OrderDto> missing = new Failure("order.not_found", "订单不存在");

string Describe(Result<OrderDto> outcome) => outcome switch
{
    Success<OrderDto> success => $"{success.Data.Id}: {success.Message}",
    Failure failure => $"{failure.Code}: {failure.Message}",
    null => throw new InvalidOperationException("结果未初始化")
};
```

移除 `IsSuccess`、`ErrorCode`、`Extra`、`IResult`、`FailResult`、
`ResultProjection`、`Result.From` 以及原来读取成功数据的 `Result<T>.Value` API。
原生联合类型仍有编译器生成的 `Value`，但它表示选中的**分支对象**，不是业务数据。
匹配成功分支后使用 `success.Data`。默认联合值没有分支，RPC 边界会拒绝它。

保留构造辅助方法：`Result.Success()`、`Result.Success(data, message)`、
`Result.Fail(code, message)`、`Result.Page(items, totalCount, message)` 和
`Result.Stream(items, message)`。字符串数据使用 `Result.Success<string>(data, message)`
或 `new Success<string>(data, message)`；`Result.Success("保存成功")` 表示无数据的成功消息。

## 分页和流式响应

`PaginatedResult<T>` 改为 `Result<Page<T>>`。成功数据包含 `Items` 和可选的
`TotalCount`，不再将这些信息放入 `Extra`。`StreamingResult<T>` 改为
`Result<IAsyncEnumerable<T>>`，生成客户端返回 `Task<Result<IAsyncEnumerable<T>>>`。

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
        throw new InvalidOperationException("结果未初始化");
}
```

开始传输前失败时返回普通失败 JSON。HTTP 和 JSON-RPC 成功流通过 SSE 发送数据，
通过 `X-NOF-Result-Message` 响应头发送成功消息，编码为 Base64 UTF-8。
客户端先解码消息，再返回成功分支。跨域浏览器应用需要在 CORS 策略中使用
`WithExposedHeaders(StreamingResponseMetadata.MessageHeader)` 暴露这个响应头。
枚举期间的异常仍属于流传输失败，初始成功分支不保证整个流都已成功完成。
JSON-RPC 在服务端枚举异常时发送错误事件。

## JSON 协议

```json
{"kind":"success","data":{"id":42},"message":"加载成功"}
{"kind":"success","message":"保存成功"}
{"kind":"failure","error":{"code":"order.not_found","message":"订单不存在"}}
```

`kind` 必填且必须与分支一致；泛型成功分支必须包含非空 `data`。
相互矛盾的分支、未知字段以及旧的 `isSuccess/value/extra` 信封都会被拒绝。
失败没有成功数据，相同的失败 JSON 可以读取为 `Result` 或 `Result<T>`。
System.Text.Json 源生成元数据支持这些联合类型和分支记录；Native AOT 应用需要
在 JSON 上下文中注册具体结果类型和数据类型。

## RPC 边界和迁移

`IRpcResponse<TSelf>` 继承运行时 `IUnion`，并为生成的传输代码提供静态
`CreateFailure` 和 `CreateEmptyResponse` 工厂。它没有成功标志、业务数据访问器
或基于反射的转换。自定义响应联合类型需要实现这个自引用接口。
中间件失败通过 `CreateFailure` 传入具体响应联合类型；其余情况
`RpcResponse.Require<TResponse>` 要求响应类型与声明完全一致，而且必须包含分支。
它不会将任意成功对象投影成另一种响应类型。

成功 HTTP 空响应体只允许生成无数据的 `Result.Success()`，泛型响应缺失数据会被拒绝。
HTTP 非成功状态码上的带标签失败体会保留业务错误代码和消息；其他响应体使用
HTTP 数字状态码作为错误代码，并将响应文本作为诊断消息。
OAuth 端点仍返回 OAuth 标准协议格式，不使用 NOF RPC 信封。

消费者需要匹配具体成功或失败分支，将业务数据读取改为 `success.Data`，
将错误读取改为 `failure.Code/Message`，并把 `Extra` 中的业务元数据改成 DTO 的显式字段。
部署时协调升级整个主版本，或提供独立版本的 API 路由。回滚时恢复完整的 NOF 11
契约、客户端、服务端及其原有协议。

参考 [C# 联合类型](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
和 [System.Text.Json 联合类型文档](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types)。
