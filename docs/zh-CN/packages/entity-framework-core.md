# NOF.Infrastructure.EntityFrameworkCore

[English](../../en/packages/entity-framework-core.md)

原生 EF Core 上下文、租户/软删除约定，以及共享工作单元与 Outbox 存储。数据库 Provider 通过原始 options builder 配置，不包装原生查询 API。

## 注册与启动迁移

```csharp
builder.UseDbContext<AppDbContext>((creation, ef) => ef.UseNpgsql(
        DbConnectionStringTemplateResolver.ResolveTenantId(
            builder.Configuration.GetConnectionString("postgres")
                ?? throw new InvalidOperationException("Missing PostgreSQL connection."),
            creation.TenantId)))
    .ConfigureNOFDbContextOptions(options => options.TenantMode = TenantMode.DatabasePerTenant)
    .MigrateOnStart();
```

MigrateOnStart 在 Hosted 服务启动前等待宿主库迁移，失败则阻止启动。DatabasePerTenant 模式在 ApplicationStarted 后通过 ITenantProvider 顺序枚举其他租户，为每个租户创建独立作用域和上下文，后台迁移不阻塞启动；SharedDatabase 只迁移共享库。宿主与所有租户使用同一模型/迁移链，宿主专用表在租户库中可以为空。

ITenantProvider 属于 Infrastructure，替代旧 ITransactionalMessageTenantProvider，GetTenantIdsAsync 契约保持不变。默认只处理宿主；业务提供持久化目录时，需要支持并发枚举，并在查询宿主库时自行创建作用域。目录应返回需要迁移和处理 Outbox 的租户。

后台迁移跳过宿主和重复的规范化 ID，单个租户失败会记录日志并继续，目录枚举失败则结束该次迁移，关闭宿主会取消。这是每次启动一次的迁移过程，不是持续的租户开通系统。

## 宿主专用实体

在 `OnModelCreating` 或 `IEFCoreModelConfiguration` 中为根实体调用 `IsHostOnly()`。扩展方法位于 `Microsoft.EntityFrameworkCore.Metadata.Builders`。

```csharp
modelBuilder.Entity<HostSetting>().IsHostOnly();
```

在 `SharedDatabase` 模式下，NOF 会为标记的实体继承层次跳过自动 `TenantId` 映射、租户查询过滤、保存时写入租户 ID 以及按租户划分的索引。显式 EF 映射和软删除约定仍然生效。共享库中的租户上下文也能通过 EF 查询这张表，应用必须在自己的访问边界限制对宿主数据的访问。

如果共享库中的现有表已有 `TenantId` 列或按租户划分的索引，请先审查生成的迁移。将唯一索引调整为全表唯一之前，先确认哪些数据应保留在宿主表里，并处理重复值。`DatabasePerTenant` 模式仍为宿主和各租户使用同一模型和迁移链，标记过的实体表也会在租户库中创建。

## 共享会话与原生配置

同一作用域中的 AppDbContext、NOFDbContext、DbContext、IUnitOfWork 和已启用的消息存储使用同一个原生上下文；原生 SQL 可以参与 ITransactionManager 建立的事务。工厂创建的上下文独立，需要调用者按所有权释放。必须在解析作用域持久化服务之前选定租户。

UseDbContext 的回调收到原生 DbContextOptionsBuilder<T> 和 DbContextCreationContext，后者包含规范化的 TenantId、DbContextType、TenantMode 与作用域 Services。回调每次创建上下文时执行，同一类型按注册顺序组合，不同上下文类型互不覆盖。只有连接路由不同时，使用这个回调即可；同步创建需要的远程连接信息应由业务依赖预先缓存。

```csharp
builder.UseDbContext<AppDbContext>((creation, ef) =>
{
    var catalog = creation.Services.GetRequiredService<ITenantDatabaseCatalog>();
    ef.UseNpgsql(catalog.GetConnectionString(creation.DbContextType, creation.TenantId));
});
```

## 明确的租户迁移与定制工厂

ITenantDbContextFactory<T> 为显式租户创建强类型上下文，不修改 Context.Current，并复用其原生配置回调。MigrateAsync 辅助方法管理上下文释放，再调用 EF 原生迁移。业务负责目录、重试、并发上限、部署协调与数据回填。

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var factory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory<AppDbContext>>();
await factory.MigrateAsync(tenantId, cancellationToken);
```

新租户可在宿主目录中保存 Pending/Migrating/Ready/Failed 和 Schema 版本，只有迁移及回填完成后才发布 Ready。授权边界应在解析租户数据库之前检查就绪状态。进程健康与宿主库迁移成功不代表所有租户已就绪；多副本需要业务部署任务或分布式租约，数据库迁移锁不覆盖业务回填与状态发布。

NOFDbContextFactory<T> 是公开可继承类型，其创建方法可重写。只有需要改变创建生命周期时才派生工厂，再用 `UseDbContext<AppDbContext, AppDbContextFactory>(...)` 注册；它同时作为具体工厂、NOFDbContextFactory<T>、ITenantDbContextFactory<T> 和原生 IDbContextFactory<T> 解析。

## 软删除与模型迁移

软删除默认关闭。通过 ConfigureNOFDbContextOptions 的 SoftDeleteEnabled 开启支持的根实体约定，或对单个根实体调用 HasSoftDelete。原生模型、查询、连接重试、拦截器、日志与跟踪配置都使用 EF API。

```csharp
builder.UseDbContext<AppDbContext>()
    .ConfigureNOFDbContextOptions(options => options.SoftDeleteEnabled = true);
```

删除改为时间戳更新，正常查询隐藏已删除数据；独占导航与外键处理遵循 Provider 约定。启用、关闭软删除或改变租户模式都可能改变模型，必须审查迁移；升级旧项目时保留原有设置。旧 WithTenantMode/WithSoftDelete/WithConnectionString/WithConnectionStringResolver/WithOptions 和 DbContextConfigurationOptions 已删除。配置入口收敛本身不会重写数据。

Outbox 只有明确 AddOutbox 才启用；消息端点不要求消费数据库，业务自行保存并实现幂等。参见[原生 EF 配置](../design/efcore-configuration.md)、[持久化边界](../design/persistence-boundaries.md)和[Outbox 迁移](../design/transactional-outbox.md)。
