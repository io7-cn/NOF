# NOF 12 的 Result 联合类型

[English](../../en/design/result-unions.md) · [开发指南](../README.md)

NOF 12 在 .NET 11 上使用 C# 15 原生联合类型，破坏 NOF 11 的源码、二进制和
JSON 协议兼容性。契约、服务端和客户端需要一起重新构建及部署。

## 成功分支直接是业务值

```csharp
public union Result<T>(T, Failure) : IRpcResponse<Result<T>>;
public union Result(Success, Failure) : IRpcResponse<Result>;
```

`Result<T>` 直接包含 `T`。`Success<T>` 是可选的消息包装，包含非空 `Data` 和
`Message`；成功响应需要消息时，显式声明 `Result<Success<T>>`。
无业务数据的 `Result` 使用 `Success`，消息默认为空字符串。
业务 DTO 也可以自行定义消息字段，不必使用框架包装。

```csharp
Result<OrderDto> loaded = order;
Result<Success<OrderDto>> withMessage = new Success<OrderDto>(order, "从缓存加载成功");
Result<OrderDto> missing = new Failure("order.not_found", "订单不存在");
Result saved = new Success("保存成功");

string Describe(Result<OrderDto> outcome) => outcome switch
{
    OrderDto value => $"订单 {value.Id}",
    Failure failure => $"{failure.Code}: {failure.Message}",
    null => throw new InvalidOperationException("结果未初始化")
};
```

匹配成功值本身。移除 `IsSuccess`、`Extra`、`IResult`、`FailResult`、
`ResultProjection` 和 `Result.From`。编译器生成的 `Value` 是选中的值或分支对象，
应用代码使用类型模式匹配。默认联合值没有分支，RPC 边界会拒绝它。

构造辅助方法包括 `Result.Success(data)`、`Result.Fail(code, message)`、
`Result.Page(items, totalCount)` 和 `Result.Stream(items)`。
`Result.WithMessage(data, message)` 返回 `Result<Success<T>>`。
字符串数据使用 `Result.Success<string>(text)`；`Result.Success("保存成功")`
表示无业务数据的成功消息。

## 唯一的共享 Failure

`NOF.Abstraction.Failure` 是唯一的共享失败值，包含非空白 `Code` 和非 null
`Message`，构造参数顺序为 `(code, message)`。Contract 和 Domain 不再各放一份，
也不需要失败转换适配器。领域生成的失败可以直接作为任意 `Result<T>` 的返回值。
`Result.Fail(existingFailure)` 返回同一个共享实例。

`[Failure(name, message, errorCode)]` 注解仍位于 Domain，其生成器现在构造
`NOF.Abstraction.Failure(code, message)`。领域异常直接接受这个共享值。
导入 `NOF.Domain` 后，可以用 `failure.Throw()` 抛出 `DomainException`；
这是 Domain 扩展方法，因此 Abstraction 不会反向依赖 Domain。

## JSON 和 Native AOT

System.Text.Json 直接序列化选中的业务值：

```json
{"id":42}
{"data":{"id":42},"message":"从缓存加载成功"}
{"message":"保存成功"}
{"$kind":"failure","code":"order.not_found","message":"订单不存在"}
```

依次表示 `Result<OrderDto>`、`Result<Success<OrderDto>>`、无数据的 `Result`
和共享失败。原生联合类型的自定义分类器通过根级保留标记 `$kind: "failure"`
选择失败分支，不会仅凭 `code`、`message` 字段判断失败。支持标量、DTO、集合和
字典，包括包含这些同名字段的成功 DTO。

Result 协议保留根级 `$kind` 字段。如果成功数据需要这个字段，应显式使用
`Result<Success<T>>`，让它位于 `data` 内。无效标记、不完整失败以及混入业务数据
的失败对象会被拒绝。旧的 `isSuccess/value/extra` 信封不属于兼容协议。

分类器使用源生成的联合类型分支元数据，不通过反射构造闭合泛型转换器。
Native AOT 应用需要在 JSON 上下文中注册具体结果和数据类型；共享 Failure 也已
加入 NOF 内置的 JSON 元数据上下文。

## 分页和流式响应

`PaginatedResult<T>` 改为 `Result<Page<T>>`，成功值有显式 `Items` 和可选的
`TotalCount`。`StreamingResult<T>` 改为 `Result<IAsyncEnumerable<T>>`。
流需要成功消息时声明 `Result<Success<IAsyncEnumerable<T>>>`。
两种形式都支持服务端 SSE 端点，服务端调用返回对应的 `Task<Result<...>>`。
HTTP 调用方自行维护 SSE 读取逻辑，不再生成客户端。

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
        throw new InvalidOperationException("结果未初始化");
}
```

开始传输前失败时返回普通 Failure JSON。显式包装的成功流通过
`X-NOF-Result-Message` 响应头传递消息，编码为 Base64 UTF-8；应用自己维护的 HTTP 读取器可将其
还原为 `Success<IAsyncEnumerable<T>>`。跨域浏览器应用需要通过
`WithExposedHeaders(StreamingResponseMetadata.MessageHeader)` 暴露该响应头。
裸流仍然返回裸流。枚举期间的异常属于流传输失败，初始成功并不保证整个流完成。

## 边界、部署和回滚

`IRpcResponse<TSelf>` 继承 `IUnion`，提供静态 `CreateFailure` 和
`CreateEmptyResponse` 工厂，没有成功标志或反射数据投影。自定义响应联合类型
使用 Abstraction 中的共享 Failure。`RpcResponse.Require<TResponse>` 保留精确匹配
的响应类型，或通过静态工厂传播共享失败；缺少分支或成功类型不匹配时会拒绝响应。
成功 HTTP 空响应体只适用于无数据 Result。非成功 HTTP 状态码上的带标记失败会
保留业务代码和消息；其他响应体使用 HTTP 数字状态码及诊断文本。
OAuth 端点继续返回标准协议。

迁移时直接匹配成功的 `T`，需要消息时选择 `Success<T>`，错误改读
`failure.Code/Message`，并把 `Extra` 改成 DTO 显式字段。部署时协调升级整个
主版本，或提供独立版本的 API 路由。回滚时恢复完整的 NOF 11 客户端、契约、
服务端及原有协议。

参考 [C# 联合类型](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
和 [System.Text.Json 联合类型文档](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types)。
