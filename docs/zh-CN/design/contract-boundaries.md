# Contract 声明与 Application 执行边界

[English](../../en/design/contract-boundaries.md) · [预期失败](expected-failures.md) · [统一操作](unified-operations.md)

NOF.Contract 只包含声明和数据模型。其运行时程序集不引用其他 NOF 程序集，不转发执行类型，也不依赖 Application。NOF.Contract.SourceGenerator 保留原名，只承载声明分析器：在 Contract 编译时检查服务签名与 FailureCase 声明，不再生成任何代码。

| 归属 | 类型与职责 |
|---|---|
| Contract | IApplicationService、FailureCase/策略元数据特性、请求、成功 DTO、自定义错误数据、Empty/Optional/Page |
| Application | ServiceOperationReference、ServiceOperation、InvocationContext/InvocationOrigin、完整 ServiceOperationDescriptor、ServiceExecution/ServiceCompleted/ServiceRejected/RejectionReason/ServiceRejectedException、执行接口与元数据解释 |
| Abstraction | Context、不可变语义信息及异步流绑定、共享 Failure 等基础能力 |
| 宿主/基础设施 | 传输适配、身份建立、所选执行引擎、HTTP 表达、消息路由与可靠接收/重试 |

## 声明与执行

```csharp
// Contract
public interface INodeService : IApplicationService
{
    [FailureCase("Missing", "404")]
    NodeDto Read(ReadNode request);
}

// Application
public partial class NodeService : ApplicationService<INodeService>;
public sealed class ReadNodeHandler : NodeService.Read
{
    public override Task<Result> HandleAsync(
        ReadNode request, InvocationContext invocation, CancellationToken ct)
        => Task.FromResult<Result>(new Missing("节点不存在"));
}
```

FailureCase 仍标在无需 partial 的 Contract 接口方法上；Result 与具体失败分支仍生成在 Application 操作中。执行类型现在使用 NOF.Application 命名空间。语义 Context 的命名空间改为 NOF.Abstraction，与其原有程序集归属一致。

ServiceOperationReference<TRequest> 归 Application，保存契约类型、操作名、稳定 Id 和输入类型；Contract 不再生成 IServiceOperations.Method 入口。它不解析 MethodInfo，不校验方法是否存在及输入形状，也不判断 CanSubmit；Application 在执行/提交边界解释它。远端生产者可以显式构造 new ServiceOperationReference<SendMail>(typeof(IMailService), nameof(IMailService.Send))，只引用共享业务 Contract 和框架 Application 包，无需依赖接收方的 Application 实现。

NodeService.Read.Descriptor 提供完整返回类型和解析后的方法元数据，其 Reference 属性给出生产者所用的声明引用。ServiceOperationInspector.ValidateReference 集中校验引用；CanSubmit 检查是否返回 Empty 且没有 FailureCase；RequiresHostTenant 解释特性及等价策略元数据。默认消息绑定和路由调用这些检查，逻辑迁移不会绕过授权或允许丢弃业务失败。

## 迁移

- 为 ServiceOperationReference、ServiceOperation、InvocationContext、完整描述符、执行结果及拒绝类型引入 NOF.Application；Contract 不保留旧类型别名或转发。
- 为 Context 引入 NOF.Abstraction，并替换显式 NOF.Contract.Context 引用。其不可变语义和异步流恢复行为不变；Contract 中旧 TypeForwardedTo 转发入口此次明确删除。
- 将生成的 IServiceOperations.Method 改为 Service.Operation.Descriptor.Reference；远端生产者可显式构造 Application 的 ServiceOperationReference<TRequest>。稳定操作 Id 和队列路由不变。
- 需要直接检查引用时，用 Application 的 ServiceOperationInspector 替代 reference.MethodInfo/CanSubmit。正常调用和消息绑定 API 已自动检查，业务 Handler 无需重复校验。
- 将 UseHostTenantAttribute.IsRequired(method) 改为 ServiceOperationInspector.RequiresHostTenant(method)，特性声明本身仍在 Contract。
- 客户端继续只依赖 Contract 和基础能力。示例 HTTP 客户端改用原生 HttpRequestException/HTTP 状态码处理传输失败，不引用 Application 的执行拒绝异常。

同步更新运行时、Provider、宿主与生成器包。Contract DTO、失败声明、Application 分支名及可选 JSON union 判别格式不变；本次分层和命名空间调整不需要迁移数据库或已存储消息路由。

边界测试验证 Contract 没有其他 NOF 运行时程序集依赖、没有转发执行类型或生成操作引用，并检查解释逻辑、执行模型和语义 Context 的正确归属。原有执行、授权、消息、HTTP 与生成器隔离回归继续保留。
