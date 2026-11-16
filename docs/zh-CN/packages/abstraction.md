# NOF.Abstraction

[English](../../en/packages/abstraction.md)

共享上下文、身份、权限检查与本地事件。

Context、不可变权限策略、IUserContext、IEventPublisher 和 LocalEventHandler<T> 构成共享层。AddNOFAbstraction 注册容器内默认服务；受控入口绑定 Ambient 便捷接口，普通依赖注入作用域不需要激活协议。Handler 有异步操作时优先显式异步发布事件。JSON 元数据按宿主配置，JsonSerializerOptions.CreateNOF 返回独立默认实例。Failure 提供 Title/Detail，预期业务失败使用生成分支。

## 安装

```sh
dotnet add package NOF.Abstraction
```

## 用法与边界

- [authorization](../design/authorization.md)
- [execution-context](../design/execution-context.md)
- [ambient-binding](../design/ambient-binding.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
