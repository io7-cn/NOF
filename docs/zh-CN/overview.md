# NOF 框架

[English](../en/overview.md)

NOF（Neat Opinionated Framework）是面向 .NET 业务应用的框架，划分 Domain、Contract、Application、Infrastructure 与原生宿主边界。运行时项目面向 .NET 11/C# 15，生成器保持 netstandard2.0。下一个 NOF 主版本为 12，已发布版本保持不可变。

## 主要能力

- 契约服务操作与独立应用操作，自动注册 Handler。
- Application 为声明失败生成原生 C# 联合类型，不提供通用 Contract Result 或生成客户端。
- 业务执行前统一检查权限、租户及宿主选择的调用 Filter。
- 显式原生 HTTP 端点模块、资源/ProblemDetails 映射、OpenAPI 与服务器发送事件（SSE）。
- 生产者事务性 Outbox；RabbitMQ/Memory 直接消费，幂等性由业务负责。
- 领域值对象、只读集合、表达式映射和作用域工作单元端口。
- 宿主显式选择缓存、对象存储、Backplane 广播与 ID Provider。
- 原生 ASP.NET Core 身份集成，宿主配置 OpenTelemetry 导出。
- Blazor 权限/浏览器服务、MAUI/WebAssembly 装配与隔离测试宿主。

## 从维护中的 Sample 开始

[能力工作台](design/sample-workbench.md)运行两个 NOF 服务，以及 RabbitMQ、PostgreSQL、Garnet 和外部 Keycloak 身份提供者。主要能力都有可见操作入口，不发送真实邮件；启动、账号、路由和升级注意事项均在指南中。

自己的应用在 Contract 声明模型，在 Application 实现生成操作，在基础设施实现业务拥有的持久化/传输端口，由宿主显式装配。请参考[完整配置 CRUD 示例](design/configuration-rest.md)与[包指南](packages/index.md)，不要复制已移除的注册 API。

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();
// 原生 Build/UseNOF 之后，由应用的端点模块映射操作。
```

短示例中的类型属于业务应用；完整可运行装配见 Sample。Contract 声明不会自动暴露任何传输端点。

## 开发与兼容性

使用 global.json 固定的 SDK/工作负载与 Directory.Packages.props 中央版本。原生持久化保留其中选定的兼容 EF/Provider 组合。遵循[贡献指南](contributing.md)、[代码组织](development/code-organization.md)、[架构](design/index.md)与[安全报告](security.md)。移动源文件时，公开负载名称与序列化格式仍是兼容性边界。安装包时选择已发布版本，不能假设尚未发布的源码已经可从 NuGet 获取。

许可证为 [Apache-2.0](../../LICENSE)。
