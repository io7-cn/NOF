# NOF 12 操作专属结果 Union

[English](../../en/design/result-unions.md) · [声明失败](expected-failures.md)

NOF 12 使用 .NET 11 和 C# 15 原生 Union。Contract 不再提供通用 Result、Result<T>、
相关工厂或 ResultJsonTypeClassifier；Success 和 Success<T> 包装也已删除。
契约、实现和调用方需要同步升级，不提供兼容别名。

在 partial 服务契约上声明成功业务值及预期失败：

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
        => Task.FromResult<Result>(new NotFound("订单不存在"));
}
```

这里 Result 是继承的 IOrderService.GetOperation.Result，包含 OrderDto 和此操作专属的
NotFound 分支，不是通用结果。Handler 直接返回 DTO、Empty.Instance 或生成的失败分支。
Page<T> 和 IAsyncEnumerable<T> 都是普通业务值，成功消息属于业务 DTO。

无声明失败的操作直接返回 DTO、可空值、Empty 或应用自己的 Union。非 IApplicationService
形态的协议接口可在所属包内声明具体业务 Union，例如 OAuth 令牌、客户端和设备服务。
不会把通用 Result 换个名字放到框架其他层。示例 UI 的 UiResponse 属于应用自有展示接口。
OAuth 应用服务与其他应用服务一样使用 FailureCase。

穷尽性由 C# 编译器检查 Union switch 表达式；NOF 不增加结果标记或检查分析器。
生成的 Match 保留为可选便利方法。默认 Union 内容仍在调用边界被拒绝。

生成失败的 JSON 使用 $failure 分支名，携带 errorCode/message 或自定义错误数据。
原生 HTTP 适配器显式映射到资源和 ProblemDetails，没有强制响应包装，消费者自行提供
JSON 元数据。生成器与 AOT 限制参见声明失败指南。

删除 DictionaryExtensions.CreateOrCopy 和 ContextExtensions.CopyHeadersFrom/ReplaceHeadersFrom。
直接构造字典，执行数据使用 Context.WithItem/WithItems；传输适配层负责解码消息头。
集成测试继续覆盖消息头大小写和租户隔离行为。
