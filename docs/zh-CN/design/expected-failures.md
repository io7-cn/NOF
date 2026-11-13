# 声明预期失败

[English](../../en/design/expected-failures.md) · [应用服务](application-requests.md) · [统一操作](unified-operations.md)

Contract 接口方法继续用 FailureCase 声明全部可预期失败；Application 在 Service.Operation 中生成具体 Result 联合类型和失败分支。Contract 接口无需 partial，也不会被生成器补充嵌套类型。

```csharp
// Contract：只负责声明成功类型、失败分支及其数据。
public sealed record DeleteConflict(long NodeId);
public interface INodeService : IApplicationService
{
    [FailureCase("NotFound", "404")]
    [FailureCase("HasChildren", typeof(DeleteConflict))]
    Empty Delete(DeleteNodeRequest request);
}

// Application：不需要额外声明操作类或失败承载类。
public partial class NodeService : ApplicationService<INodeService>;
public sealed class DeleteNode : NodeService.Delete
{
    public override Task<Result> HandleAsync(
        DeleteNodeRequest request, InvocationContext invocation, CancellationToken ct)
        => Task.FromResult<Result>(new NotFound("节点不存在"));
}
```

生成的 NodeService.Delete 直接继承 `ServiceOperation<DeleteNodeRequest, NodeService.Delete.Result>`，并拥有 Result、NotFound 和 HasChildren。Handler 继承这些类型，继续使用 `Task<Result>` 和 new NotFound(...)。不同 ApplicationService 实现生成各自的具体结果类型；共享声明和数据类型仍位于 Contract。

## 名称、错误码与自定义数据

```csharp
[FailureCase("NotFound")]               // ErrorCode 默认使用 NotFound。
[FailureCase("NotFound", "000404")]     // 错误码是不透明字符串，不参与类型命名。
[FailureCase("HasChildren", typeof(DeleteConflict))] // 自定义 Error，不强加错误码。
```

必填 Name 是唯一且合法的 C# 分支标识符。可选第二参数为非空错误码或 typeof(CustomError)，构造函数重载保证两者互斥。Contract 的 FailureCaseAnalyzer 使用 NOF220 报告非法名称、关键字、成员冲突、重复名称和不合法声明。错误码允许纯数字或其他不适合命名的内容，不同分支也可以共享错误码。Attribute 只能标在 IApplicationService 接口的方法上，不能改标在实现方法上。

默认分支直接提供 ErrorCode、Message，构造函数的 message 可省略。自定义分支提供强类型 Error，不强加 ErrorCode 或 Message。每个 Name 都是独立 sealed 分支，即使数据类型相同也不合并。不生成开放的共享 Failure 分支、Create 工厂或空壳失败承载类。

直接返回成功值或已声明分支；未声明错误无法隐式转换为该闭合 union。成功消息属于业务 DTO。FailureCase 方法保留单一请求、同步且不可空的成功类型约束，契约为顶层非泛型接口；不声明 FailureCase 的操作仍支持可空返回。

## 操作引用与完整执行描述符

Contract 不再生成代码。NOF.Contract.SourceGenerator 保留原名和声明分析器。ServiceOperationReference<TRequest> 归 Application；使用 Service.Operation.Descriptor.Reference，或为远端生产者显式构造引用，无需引入接收方实现。

Application 生成 `NodeService.Delete.Descriptor`，类型为 `ServiceOperationDescriptor<DeleteNodeRequest, NodeService.Delete.Result>`。HTTP、本地调用及操作映射使用这个完整返回类型；NodeService.Delete.InvokeAsync 返回相同 Result。完整描述符的 Reference 属性可用于只需要输入的生产者路由。两者拥有相同稳定 Id，不再要求是同一对象实例。

消息消费绑定仍只接受实际返回 Empty 的完整描述符。发送虽然使用输入引用，也会检查 Contract 声明：成功类型是 Empty、但声明了 FailureCase 的操作仍可能失败，不能作为无返回任务提交；Queue/Subscription/RouteToQueue 都会拒绝这种配置。手写一个只包含成功类型的描述符也不能绕过检查。

## 处理方式与分层

使用 C# 原生 union switch 表达式检查穷尽性（CS8509），也可以使用生成的 Match。丢弃结果、兜底分支及 switch 语句仍遵循普通 C# 规则。NOF 不增加 checked-result 分析器或 ExpectedOutcome 标记；未初始化 union 在执行边界无效。

Domain 返回自己的业务值，不引用 Contract；Application 将其映射到已声明分支。技术异常与取消继续传播，不变量误用使用 .NET 标准异常。Abstraction.Failure 和 ServiceRejected 单独表达执行策略拒绝，不混入已完成的业务结果。

HTTP 模块显式把 Application 分支映射成 TypedResults、资源或 ProblemDetails。配置节点 CRUD 和 OAuth 示例适配器均使用 Application 拥有的类型。Result 是进程内结果，不是强制的网络包装。

## JSON 与 Native AOT

生成的 union 保留无需反射的分类器。可选 JSON 编码仍使用保留的 $failure Name 判别字段，以及 errorCode/message 或自定义 error 数据；未知分支、错误标记及不匹配错误码会被拒绝。成功值直接序列化。

若显式序列化生成的 union，先编译其所属 Application 程序集，再在消费它的宿主 JsonSerializerContext 中注册 `Service.Operation.Result`。不能依赖 System.Text.Json 生成器在同一轮编译中读取其他生成器的输出。原生 HTTP 端点也可以直接将结果映射成已有 DTO/ProblemDetails。冒烟测试覆盖独立编译的 Application 结果，并关闭 JSON 反射。

## 破坏性迁移

FailureCaseAttribute 继续放在 Contract 方法上，移除接口的 partial。将 `IService.<Operation>Operation.Result` 及其分支引用改为 `Service.Operation.Result` 和 Service.Operation.Case。旧 Contract 操作基类和兼容别名不保留。已经使用继承的 `Task<Result>`、new Case(...) 的 Handler 无需改变这部分写法。

生产者路由使用 Application 操作引用；HTTP、本地执行和消费者绑定使用 Application 完整描述符。此前向 Send/RouteToQueue 传入 Application 描述符的代码可以改传其 Reference。不能把接口声明的成功类型当作完整执行返回类型。

迁移时保持 JSON 分支名称、错误码与自定义数据结构稳定；此次改变 CLR 类型归属，不改变既有可选 JSON 判别格式。同步升级 Application、端点、JSON 上下文和进程内调用方。测试覆盖单体项目中两种生成器顺序，以及从独立编译 Contract 元数据生成 Application 结果。

[当前 Contract/Application/Abstraction 边界](contract-boundaries.md)。
