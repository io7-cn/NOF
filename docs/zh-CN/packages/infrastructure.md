# NOF.Infrastructure

[English](../../en/packages/infrastructure.md)

执行、可选 Provider 与事务性 Outbox。

AddNOF 装配执行与公共策略；AddCacheService、AddObjectStorage、AddBackplane、AddIdGenerator 显式选择可选 Provider。内存状态属于单个宿主。AddOutbox 开启发件暂存、存储与 Worker，消息端点映射不启用持久化；消费幂等与提交由业务负责。InMemoryInvocationPipeline 执行生效的 Global/Service/UseCase Filter 并校验结果类型。认证与遥测导出由原生宿主负责。

## 安装

```sh
dotnet add package NOF.Infrastructure
```

## 用法与边界

- [host-composition](../design/host-composition.md)
- [invocation-extensions](../design/invocation-extensions.md)
- [transactional-outbox](../design/transactional-outbox.md)
- [object-storage](../design/object-storage.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
