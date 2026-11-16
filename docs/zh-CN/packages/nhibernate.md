# NOF.Infrastructure.NHibernate

[English](../../en/packages/nhibernate.md)

原生 NHibernate 持久化与 Outbox 集成。

UseNHibernate 选择原生配置、连接与模型回调。基础设施用 ISession 实现领域仓储/查询端口，IUnitOfWork 与可选事务和 Outbox 存储共享作用域 Session。租户与值对象映射通过 Provider 配置。DatabasePerTenant 为宿主与租户使用同一模型/Schema，宿主专用表在租户库中可以为空。框架不包装原生查询 API，也不保存消费收据。

## 安装

```sh
dotnet add package NOF.Infrastructure.NHibernate
```

## 用法与边界

- [persistence-boundaries](../design/persistence-boundaries.md)
- [transactional-outbox](../design/transactional-outbox.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。

## 原生配置

目前不支持 SharedDatabase。可选的结构初始化使用 NHibernate 原生 SchemaUpdate；启用前应审查结构变更及其部署职责。

```csharp
builder.UseNHibernate()
    .WithConnectionString("Data Source=nof-{tenantId}.db")
    .WithOptions((configuration, connectionString) =>
    {
        configuration.DataBaseIntegration(db =>
        {
            db.ConnectionString = connectionString;
            db.Dialect<SQLiteDialect>();
            db.Driver<SQLite20Driver>();
        });
    })
    .BuildSchemaOnInitialize();
```
