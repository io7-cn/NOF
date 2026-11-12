# 声明可预期失败

[English](../../en/design/expected-failures.md) · [应用服务](application-requests.md)

在 Contract 方法上使用 `FailureCase` 声明可预期的业务失败。方法返回类型表示成功值，
Contract 生成器生成封闭的 `IService.OperationResult` Union；Application 生成器在
操作 override、映射、Descriptor 和 InvokeAsync 中统一使用该类型。

```csharp
public sealed record DeleteConflict(long NodeId);

public partial interface INodeService : IApplicationService
{
    [FailureCase("404", Name = "NotFound")]
    [FailureCase<DeleteConflict>("10001/?", Name = "HasChildren")]
    Empty Delete(DeleteNodeRequest request);
}
```

错误码是原样保存的非空白字符串，可以是纯数字，也可以包含不适用于 C# 标识符的字符。
分支名由独立的 Name 参数声明。一个操作有多个失败时，每个失败都必须显式指定唯一 Name。
单个未命名声明，例如 `[FailureCase("404")]`，固定生成 Failed，不从错误码推导标识符。
非泛型 Attribute 默认使用内置 `NOF.Contract.Error`（包含 Message）；泛型参数选择错误
携带的数据类型，不决定生成分支的名称。

生成结果概念上是：

```csharp
// 位于生成的 partial INodeService 声明中：
public union DeleteResult(Empty, DeleteResult.NotFound, DeleteResult.HasChildren);
// 生成的嵌套 record 携带 Error 数据、稳定的 Code 常量和 JSON 元数据。
```

每个错误码都有独立分支，即使携带的数据类型相同也不会合并。不附加开放的共享 Failure
兜底分支。Handler 返回 Empty.Instance、成功 DTO 或已声明分支；未声明的失败不能隐式
转换为此结果。不需要手写承载静态失败成员的空壳类。

```csharp
public partial class NodeService : ApplicationService<INodeService>;

public sealed class DeleteNode : NodeService.Delete
{
    public override Task<INodeService.DeleteResult> HandleAsync(
        DeleteNodeRequest request, Context context, CancellationToken ct)
        => Task.FromResult<INodeService.DeleteResult>(
            new INodeService.DeleteResult.HasChildren(new DeleteConflict(request.Id)));
}
```

调用方使用穷尽的 switch 表达式或生成的 Match 方法。Match 要求成功回调及每个失败分支
对应的回调；具名参数为 `onCase<分支名>`。增加失败会改变该签名。不要为了绕过处理而
把结果擦除为 object 或共享的无类型错误。

Contract 分析器将直接丢弃结果或 Task 报为 NOF230，将遗漏分支的 switch 表达式和 switch
语句报为 NOF231，将遮蔽新增分支的兜底模式报为 NOF232。这不是语言级效果系统或完整的
数据流证明：C# 仍允许主动擦除类型、保存但不消费结果，以及抑制诊断。保留类型并向上传播
结果是合法的。默认或 null Union 值在应用调用边界仍然会被拒绝。

## 分层和传输边界

Attribute、Error 数据类型、生成器和分析器都位于 Contract。Domain 不依赖 Contract；
领域规则返回自己的业务值，由 Application 映射到 UseCase 声明的分支。已知输入失败应在
调用抛异常的不变式构造函数之前检查。不变式或编程错误仍可使用领域异常；技术异常和取消
正常传播。

删除旧 Domain FailureAttribute、静态成员承载类生成器、failure.Throw 扩展，以及接收
Failure 的异常构造函数。Abstraction.Failure 继续用于策略拒绝和现有开放 Result API，
不会隐式加入生成的封闭结果。认证和租户拒绝仍通过 ServiceRejectedException 独立传播。

HTTP 模块显式将每个分支映射到原生 TypedResults/ProblemDetails。配置节点示例已将 CRUD、
文件、配置和流操作改为 Match；应用自己维护的 UI 适配器显式投影到较简单的 UI 接口。

## JSON、AOT 和迁移

生成的结果包含无反射分类器，失败 JSON 带保留的 `$failure` 错误码和类型化 error 数据；
未知错误码及不匹配的分支标记会被拒绝。成功直接序列化业务值。这是可选编解码器，不是
强制 HTTP 包装；原生端点继续返回资源或 ProblemDetails。

使用源生成 JSON 元数据时，先把生成类型编译到 Contract 程序集，再在消费者的
JsonSerializerContext 中注册具体结果类型。System.Text.Json 生成器无法读取同一轮中
另一生成器的输出。单项目原生 HTTP 适配器可先将内存结果映射为已有 DTO/ProblemDetails，
无需序列化生成的 Union。测试覆盖单项目中的生成器顺序独立，以及独立契约的真实 Native AOT。

契约签名、Handler 返回类型、调用方和适配器需一同升级。未标注的 DTO、可空值和 Result
方法保持原行为。目前支持顶层、非泛型的 partial 服务接口，以及不重名、非泛型、单请求、
同步非空成功值的方法；可选成功值请使用显式数据类型。不能预先保证比返回 Result 更快：
避免异常控制流不代表没有分配，具体性能仍需基准测试。
