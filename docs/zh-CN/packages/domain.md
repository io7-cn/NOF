# NOF.Domain

[English](../../en/packages/domain.md)

值对象与领域 ID 契约。

Domain 保留业务不变量、IValueObject<T>、IIdGenerator 与可空的 IdGenerator.Current。实现 IsValid(value, out errorMessage) 和可选 Normalize，From/TryFrom 由生成器提供。ReadOnlyCollection 生成缓存的实时只读集合视图。Domain 不注册服务，也不依赖 Contract 或传输层；技术性误用使用标准异常，业务拒绝由 Application 转换为失败分支。

## 安装

```sh
dotnet add package NOF.Domain
```

## 用法与边界

- [value-objects](../design/value-objects.md)
- [value-object](../design/value-object.md)
- [unified-operations](../design/unified-operations.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
