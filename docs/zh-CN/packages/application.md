# NOF.Application

[English](../../en/packages/application.md)

操作、业务端口与表达式映射。

ApplicationService<TService> 容器提供生成的操作基类，具体 OperationHandler 也可表达不公开为 Contract 方法的内部反应。Handler 接收 InvocationContext 与 CancellationToken。完整调用返回 object，因为生效的 Filter 也可能返回额外的强类型失败。Application 持有聚合/查询协作、IUnitOfWork、IOutbox、ICacheService 和 IObjectStorage 契约，原生 Provider 留在基础设施。映射用同一注册表达式完成查询投影和内存转换。

## 安装

```sh
dotnet add package NOF.Application
```

## 用法与边界

- [application-requests](../design/application-requests.md)
- [mapper](../design/mapper.md)
- [persistence-boundaries](../design/persistence-boundaries.md)
- [transactional-outbox](../design/transactional-outbox.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
