# ADR 0001：应用执行核心与原生适配器

[English](../../en/design/0001-application-core-and-adapters.md)

- 状态：已接受，下面描述当前实现。
- 决策日期：2026-11-11。审查日期：2026-11-16。
- 责任人：NOF 维护者（XmmShp）。
- 跟踪：[issue #4](https://github.com/io7-cn/NOF/issues/4)；前置 [.NET 迁移 PR #3](https://github.com/io7-cn/NOF/pull/3)。

## 背景与选项

NOF 面向 HTTP、本地调用、队列消费和后台工作。以远程调用（RPC）为中心的核心使业务依赖传输结果和重复的中间件模型。候选方案包括保留原核心、增加 REST 包装，或使用强类型应用执行核心配合原生适配器。采用第三种方案：保留熟悉的服务操作 override 模型，把协议决策交给所属宿主。

## 决策

Contract 包含同步服务声明、请求/响应模型、FailureCase 和权限注解，不生成代码，也不要求 partial 接口。Application 生成 Service.Operation 基类、封闭 Result/失败分支、描述符与注册。操作以“服务 + 方法”标识，可以共享输入/输出。独立 OperationHandler 表达不公开为 Contract 方法的内部反应。

Handler 返回业务值或声明的失败，由原生联合类型 switch 表达式进行穷尽检查。完整调用也可以返回生效宿主 Filter 的失败，执行器校验允许结果。技术异常和取消继续传播；Domain 保留不变量与业务值，不依赖传输或 Contract。

Infrastructure 提供可选执行管道以及 Global/Service/UseCase Filter 计划。权限和租户检查先于 Handler 构造，额外校验和事务 Filter 由宿主选择。消费提交和幂等性仍属于应用职责，执行器和传输映射都不会隐式保存业务状态。

| 职责 | 所属边界 |
| --- | --- |
| 路由、绑定、状态/Body/Header、ProblemDetails、SSE 与 OpenAPI | 原生 HTTP 端点模块与 ASP.NET Core |
| 业务操作标识、强类型结果和注册 | Application 及其生成器 |
| 身份协议、令牌验证、宿主生命周期与遥测导出 | 现成提供者与原生宿主 API |
| 权限声明 / 当前授予 / UI 检查 | Contract / Abstraction 授予来源 / 显式 UI 策略 |
| 聚合写入和 DTO 查询端口、工作单元契约 | Domain/Application；原生持久化实现位于基础设施 |
| 可靠发件意图 | 显式 AddOutbox 与强类型基础设施 OutboxHandler |
| 队列/交换机拓扑、重试/死信和确认 | 原生传输配置；NOF 只将操作映射到队列 |
| 消费幂等与副作用原子性 | 业务约束、事务或提供者幂等契约 |
| 缓存、存储、广播与 ID 算法 | 显式可选 Provider |

HTTP 配置按精确类型覆盖整条响应映射，只保留状态码便捷写法；特殊表示使用普通 C#。不保留 Contract HttpEndpoint 注解、旧 RPC 适配器、命名响应变体或客户端生成。运行时 OpenAPI 读取最终元数据，不执行业务展示回调。

Outbox 将完整的 JSON 标量 Context、显式 Headers、稳定 ID 与追踪一起快照，原生发布器直接转发已准备好的消息头。消费端恢复上下文，在新作用域内等待映射操作完成。框架不提供 Inbox、消费收据数据库、顺序投递层或 MessageBinding。原生 ACK 在执行完成之后发送，包括 Handler 选择的业务提交。

## 影响与迁移

框架公开面缩小，业务对传输的依赖降低。宿主显式选择 Provider，维护端点/拓扑模块与远程集成，因此需要原生集成测试以及明确的业务幂等性，不保证外部副作用严格执行一次。UI 权限不能代替服务端授权，持久化消息不序列化实时 Principal 或依赖注入作用域。

同时更新 Contract、Application、原生适配器与调用方。除非经过审查的数据迁移明确改变，否则保留负载名称与线上数据。删除旧 Inbox 前解决遗留工作，备份受影响数据库，并审查原生 Schema/身份迁移；回滚恢复匹配程序与备份。合并仍需必需检查与独立审查，本决策不构成发布授权。

## 维护中的实现指南

- [应用操作](application-requests.md)与[独立操作](unified-operations.md)
- [Contract 边界](contract-boundaries.md)与[预期失败](expected-failures.md)
- [HTTP 适配](http-adaptation.md)、[调用 Filter](invocation-extensions.md)与[授权](authorization.md)
- [Outbox 与迁移](transactional-outbox.md)、[原生 EF](efcore-configuration.md)与[原生宿主](native-hosting.md)
- [生成器隔离](source-generators.md)
