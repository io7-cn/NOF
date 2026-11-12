# 声明可预期失败

[English](../../en/design/expected-failures.md) · [应用服务](application-requests.md)

在 Contract 方法上使用 `FailureCase` 声明可预期的业务失败。方法返回类型表示成功值，
Contract 生成器生成封闭的 `IService.<Operation>Operation.Result` Union；Application 生成器在
操作 override、映射、Descriptor 和 InvokeAsync 中统一使用该类型。

```csharp
public sealed record DeleteConflict(long NodeId);

public partial interface INodeService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    [FailureCase("HasChildren", typeof(DeleteConflict))]
    Empty Delete(DeleteNodeRequest request);
}
```

第一个参数固定为必填的 C# 分支名。第二个参数可选，且只能选择非空白的错误码字符串
或 `typeof(自定义错误类型)`：

```csharp
[FailureCase("NotFound")]               // ErrorCode 默认等于 "NotFound"
[FailureCase("NotFound", "404")]        // 显式 ErrorCode
[FailureCase("HasChildren", typeof(DeleteConflict))] // 自定义数据，无 ErrorCode
```

Attribute 参数不能直接是 C# Union 值，因此用构造函数重载表达这两个互斥选项。
删除泛型 Attribute 和可设置的 Name。不合法名称、关键字、重复名称以及与生成成员
冲突都会产生 NOF220 编译错误。错误码可以是纯数字，但分支名不能为 "404"、
"not-found" 或 "class"。不同分支可以共享错误码，由必填的 Name 区分分支身份。

默认分支直接提供 ErrorCode 和 Message，构造函数接收可选消息。自定义分支提供
类型化 Error，构造函数接收其数据，不强制自定义类型提供 ErrorCode 或 Message。
不再提供共享的 Error 包装类型。

生成结果概念上是：

```csharp
// 位于生成的 partial INodeService 声明中：
public abstract class DeleteOperation : ServiceOperation<DeleteNodeRequest, DeleteOperation.Result>
{
    public union Result(Empty, NotFound, HasChildren);
    // 生成的 NotFound 和 HasChildren record 位于这里。
}
// 生成的嵌套 record 携带 Error 数据、默认错误码和消息或自定义 Error，以及 JSON 元数据。
```

每个声明名称都有独立分支，即使携带的数据类型相同也不会合并。不附加开放的共享 Failure
兜底分支。Handler 返回 Empty.Instance、成功 DTO 或已声明分支；未声明的失败不能隐式
转换为此结果。不需要手写承载静态失败成员的空壳类。

```csharp
public partial class NodeService : ApplicationService<INodeService>;

public sealed class DeleteNode : NodeService.Delete
{
    public override Task<Result> HandleAsync(
        DeleteNodeRequest request, Context context, CancellationToken ct)
        => Task.FromResult<Result>(request.Id <= 0
            ? new NotFound("节点不存在")
            : new HasChildren(new DeleteConflict(request.Id)));
}
```

Application 生成的 NodeService.Delete 继承 INodeService.DeleteOperation，因此 Handler
可直接写 Task<Result>、new NotFound(...) 或 new HasChildren(data)。不生成工厂，
不复制或派生错误类型。
ServiceOperation 与 ServiceOperation<TRequest,TResponse> 已移至 NOF.Contract，使 Contract
操作基类不依赖 Application；显式引用旧命名空间的代码需要同步迁移。

调用方使用穷尽的 switch 表达式或生成的 Match 方法。Match 要求成功回调及每个失败分支
对应的回调；具名参数为 `onCase<分支名>`。增加失败会改变该签名。不要为了绕过处理而
把结果擦除为 object 或共享的无类型错误。

穷尽性检查由 C# 编译器负责：不完整的 Union switch 表达式产生 CS8509。
NOF 不再提供结果检查分析器、ExpectedOutcome 标记、丢弃结果限制或兜底模式禁令。
原生 C# 仍允许忽略结果、使用兜底分支；switch 语句没有与 switch 表达式相同的
穷尽性警告。Match 仍作为可选便利方法，要求为每个分支提供回调。默认或 null Union
值在应用调用边界仍然会被拒绝。

## 分层和传输边界

Attribute、生成的失败类型和生成器都位于 Contract。Domain 不依赖 Contract；
领域规则返回自己的业务值，由 Application 映射到 UseCase 声明的分支。已知输入失败应在
调用抛异常的不变式构造函数之前检查。不变式或编程错误仍可使用领域异常；技术异常和取消
正常传播。

删除旧 Domain FailureAttribute、静态成员承载类生成器、failure.Throw 扩展，以及接收
Failure 的异常构造函数。Abstraction.Failure 继续用于策略拒绝和传输和协议适配器，
不会隐式加入生成的封闭结果。认证和租户拒绝仍通过 ServiceRejectedException 独立传播。

HTTP 模块显式将每个分支映射到原生 TypedResults/ProblemDetails。配置节点示例已将 CRUD、
文件、配置和流操作改为 Match；应用自己维护的 UI 适配器显式投影到较简单的 UI 接口。

## JSON、AOT 和迁移

生成的结果包含无反射分类器，失败 JSON 带保留的 `$failure` 分支名，以及
`errorCode`/`message` 或自定义 `error` 数据；
未知分支名及不匹配的分支标记会被拒绝。成功直接序列化业务值。这是可选编解码器，不是
强制 HTTP 包装；原生端点继续返回资源或 ProblemDetails。

使用源生成 JSON 元数据时，先把生成类型编译到 Contract 程序集，再在消费者的
JsonSerializerContext 中注册具体结果类型。System.Text.Json 生成器无法读取同一轮中
另一生成器的输出。单项目原生 HTTP 适配器可先将内存结果映射为已有 DTO/ProblemDetails，
无需序列化生成的 Union。测试覆盖单项目中的生成器顺序独立，以及独立契约的真实 Native AOT。

契约签名、Handler 返回类型、调用方和适配器需一同升级。未标注的 DTO、可空值和业务 Union
方法保持原行为。目前支持顶层、非泛型的 partial 服务接口，以及不重名、非泛型、单请求、
同步非空成功值的方法；可选成功值请使用显式数据类型。不能预先保证比返回 Result 更快：
避免异常控制流不代表没有分配，具体性能仍需基准测试。

旧 `[FailureCase("404", Name = "NotFound")]` 改为 `[FailureCase("NotFound", "404")]`；
泛型错误声明改为 `[FailureCase("Conflict", typeof(MyError))]`。默认分支的 `.Error.Message`
改为 `.Message`，静态 `.Code` 改为实例 `.ErrorCode`。自定义分支保留 `.Error`，不再生成
业务错误码。JSON 分支标记从旧错误码改为 Name，序列化调用方需同步迁移。
