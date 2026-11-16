# 公开编程模型

[English](../../en/design/public-api.md)

Contract 只包含声明与数据模型。Application 持有生成的操作基类、描述符和封闭结果；Infrastructure 选择执行 Filter 与持久化能力；原生宿主负责认证、传输和生命周期。Domain 保留业务不变量，不依赖传输或 Contract。

| 能力 | 公开入口 |
| --- | --- |
| 服务操作 | `IApplicationService`、`ApplicationService<TService>`、生成的 `Service.Operation` |
| 内部操作 | `OperationHandler<TRequest>` 或 `OperationHandler<TRequest,TResponse>` |
| 预期失败 | Contract `FailureCase`；Application 生成的 `Result` 与实现 `IFailure` 的独立分支 |
| 完整本地调用 | `Service.Operation.InvokeAsync` 或 `IOperationExecutor.ExecuteAsync` |
| HTTP | 原生端点模块及 `MapGet<Operation>` 等映射 |
| 可靠发件 | `IOutbox`，每种负载一个具体 `OutboxHandler<T>` |
| 持久化 | 业务自行定义的端口、`IUnitOfWork` 与可选事务接口 |
| 共享上下文 | 不可变 `NOF.Abstraction.Context`，Items 只包含 JSON 标量 |

服务接口不需要 partial，方法声明成功业务类型；void 表示不回信，Empty 保留回复与预期失败。宿主策略拒绝不同于业务失败，应匹配具体分支，不比较显示文本；成功消息属于明确的数据模型。

框架不再提供通用 Contract Result、生成客户端、旧 HTTP RPC 兼容适配器、Inbox、Domain 异常层级或 OIDC 服务端包。使用原生 C# 联合类型匹配，由宿主将 HTTP 映射为资源或 ProblemDetails。破坏性升级必须配套更新各层和调用方，持久化负载重命名需要数据迁移。

参见[Contract 边界](contract-boundaries.md)、[预期失败](expected-failures.md)、[HTTP 适配](http-adaptation.md)和[包索引](../packages/index.md)。
