# 声明式预期失败

[English](../../en/design/expected-failures.md) · [HTTP 适配](http-adaptation.md)

Contract 在非 partial 接口方法上声明可预期的业务失败。Application 在 Service.Operation 内生成闭合 Result 联合类型和独立 sealed 分支；Contract 只保留声明和数据模型。

```csharp
public sealed record DeleteConflict(long NodeId);
public interface INodeService : IApplicationService
{
    [FailureCase("NotFound", "Node not found")]
    [FailureCase("HasChildren", "Node has children", typeof(DeleteConflict))]
    Empty Delete(DeleteNodeRequest request);
}
public partial class NodeService : ApplicationService<INodeService>;
public sealed class DeleteNode : NodeService.Delete
{
    public override Task<Result> HandleAsync(
        DeleteNodeRequest request, InvocationContext context, CancellationToken ct)
        => Task.FromResult<Result>(new HasChildren(new DeleteConflict(request.Id), "Delete the children first."));
}
```

Name 必须是唯一、合法的 C# 分支标识符，无效声明由 NOF220 报错。Title 是非空展示摘要，与标识符独立；`[FailureCase("NotFound")]` 默认使用 Name。自定义 Error 类型是摘要之后的第三个参数。摘要可以含空格、数字，不同分支可以相同。禁止比较 Title 来识别失败或推断 HTTP 状态。

所有生成分支实现 Abstraction.IFailure（Title、Detail）；自定义分支额外实现 IFailure<TError> 并提供 Error。默认分支构造器接收可选 detail；自定义分支接收必需 error 和可选 detail。共享 Failure 同样使用 Title/Detail。不保留 Code、ErrorCode、Message 兼容别名，不生成 CreateCase 工厂、空壳失败承载类或开放 IFailure 联合分支。成功消息放在业务 DTO 中。

Handler 直接继承 Task<Result> 和 new Case(...) 的写法；Result 只接受声明的成功值和失败类型。穷尽检查使用原生 C# union switch expression，NOF 不增加检查结果分析器。技术异常与取消继续传播。Domain 返回自己的业务值，由 Application 映射成声明分支，Domain 不依赖 Contract。

Service.Operation.Descriptor 包含完整业务 Result。HandleAsync 返回 Task<Result>，完整 InvokeAsync 返回 Task<object?>，因为有效宿主 Filter 也可以返回自己的具体失败。InvocationResult.Value 解包业务 union。未初始化 union 和未声明的失败在调用边界被拒绝。不使用 FailureCase 的方法仍可返回可空值或原生 union；原生 union 分支目录来自类型声明及构造器，不分析用户控制流。

操作基类通过静态 Descriptor/Describe 实现 IServiceOperationDefinition，因此可以写 `group.MapDelete<NodeService.Delete>("/{id}")`。HTTP 默认将声明的 IFailure 映射为 422 ProblemDetails；404/409 等按具体分支类型显式配置。线上只返回原生资源或 ProblemDetails，不套 Result 信封，不重复输出 code/message 扩展。参见 [HTTP 适配](http-adaptation.md)。

partial Filter 也可以声明 FailureCase，生成静态 FailureCases 目录，但不生成 Filter.Result。有效调用计划只描述启用的失败，不构造 Handler 或 Filter。参见[调用扩展](invocation-extensions.md)。

可选的 union JSON 使用保留的 $failure Name 判别字段、title/detail 及可选 error。先编译 Application，再在消费宿主的 JsonSerializerContext 注册 Service.Operation.Result；JSON 生成器不能读取同一次编译中另一个生成器的输出。HTTP 也可以直接序列化已声明的 DTO/ProblemDetails。关闭反射的冒烟测试覆盖这两种路径。

迁移时将 ErrorCode/Code 改为 Title、Message 改为 Detail，自定义属性改为 `(name, title, typeof(Error))`，同步升级序列化器与消费端。按独立 CLR 分支判断失败。OAuth 客户端仓储、注册准入端口返回 OAuthClientNotFound/OAuthClientConflict 等闭合分支，使用类型模式；远程适配器用 HttpTransportFailure 的真实 HttpStatusCode。OAuth 标准协议错误字符串继续作为协议数据，由 HTTP 边界显式映射。
