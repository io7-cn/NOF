# .NET 11 迁移与联合类型

[English](../../en/design/net11-migration.md)

NOF 12 面向 .NET 11 和 C# 15。NOF 包版本独立于 .NET 版本，已发布的 NOF 11 保持不可变。SDK 与工作负载集由 `global.json` 固定；生成器和代码修复仍面向 `netstandard2.0`，Roslyn 版本使用 `Directory.Packages.props` 中的配置。

## 构建与持久化

安装固定 SDK 与 `NOF.slnx` 要求的平台工作负载，按[贡献指南](../contributing.md)执行恢复、Release 构建、格式和测试。Windows 包含 MAUI；Linux 使用仓库中的平台替代实现，并在持续集成中验证 Native AOT（提前编译为原生程序）。

仓库在 `Directory.Packages.props` 中固定兼容的 EF Core/PostgreSQL Provider 组合。运行时目标不要求数据库依赖与 .NET 使用相同主版本；应配套升级 Provider 和 ORM，并在部署前审查原生迁移。

## 当前联合类型边界

| 边界 | 当前模型 |
| --- | --- |
| 声明式业务失败 | Contract 方法声明成功类型与 `FailureCase`；Application 生成封闭的 `Service.Operation.Result` 和失败分支。 |
| 授权 | `InboundAuthorizationResult` 表示允许/拒绝；策略先于 Handler 构造执行。 |
| 数据缺失 | `Optional<T>` 保留明确的存在状态，包括“存在且为 null”，不改成失败联合类型。 |
| 流与分页 | `IAsyncEnumerable<T>` 和 `Page<T>` 是业务值，HTTP 使用原生 SSE 或资源映射。 |
| 身份协议 | 外部提供者与原生 ASP.NET Core 认证负责 OIDC/JWT，NOF 不包含 OAuth 协议结果层级。 |

通用 Contract `Result`/`Result<T>`、Success 包装与客户端生成已删除。原生 C# 联合类型 switch 表达式负责穷尽检查，NOF 不增加 checked-result 分析器。调用边界拒绝未初始化联合类型；完整调用还可能返回生效 Filter 的失败，检查装箱业务结果时使用 `InvocationResult.Value`。

## 配套升级

同时更新 Contract、Application、适配器和调用方。保留负载类型标识与持久化消息头；公开负载重命名需要明确迁移持久化键。用业务值或生成分支替换旧 Result 工厂，并按具体类型映射 HTTP 响应。备份数据库，审查 Outbox、旧 Inbox 与身份迁移；回滚使用匹配的程序、调用方和数据库备份，不混用不同代实现。

参见[操作结果](result-unions.md)、[预期失败](expected-failures.md)、[Outbox 迁移](transactional-outbox.md)、[原生 EF 配置](efcore-configuration.md)和[宿主装配](host-composition.md)。
