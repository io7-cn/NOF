# NOF.Test

[English](../../en/packages/testing.md)

具有显式作用域与调用工具的原生测试宿主。

Host.CreateApplicationBuilder().AddNOFTest 选择隔离的内存能力；添加所有 ApplicationPart 与服务后 BuildTestHost。Build 不会启动 Hosted 服务，迁移或 Worker 测试需要显式启动原生宿主。NOFTestScope 配置租户、已验证身份、追踪和标量 Context 数据，绑定 Ambient 访问器并解析依赖。CallAsync 等待生成操作调用，不涉及生成的本地客户端。测试需要 Outbox 或原生数据库时应显式启用。

## 安装

```sh
dotnet add package NOF.Test
```

## 用法与边界

- [application-requests](../design/application-requests.md)
- [ambient-binding](../design/ambient-binding.md)
- [persistence-boundaries](../design/persistence-boundaries.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
