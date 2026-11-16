# 原生 EF Core 配置与 NOF 策略

[English](../../en/design/efcore-configuration.md) · [持久化边界](persistence-boundaries.md)

NOF 保留租户感知的 Context 创建、可选软删除、值对象约定、作用域工作单元/事务及原生 Outbox 存储。Provider、连接、日志、拦截器、重试、跟踪和模型映射交给 EF Core，通过一个原生配置回调完成，不再由 NOF 的逐项方法转发。

```csharp
builder.UseDbContext<ConfigurationDbContext>((creation, ef) =>
{
    var connections = creation.Services.GetRequiredService<ITenantDatabaseCatalog>();
    ef.UseNpgsql(connections.GetConnectionString(creation.DbContextType, creation.TenantId),
        postgres => postgres.EnableRetryOnFailure());
    ef.EnableDetailedErrors();
    ef.AddInterceptors(creation.Services.GetRequiredService<AuditInterceptor>());
})
.ConfigureNOFDbContextOptions(nof =>
{
    nof.TenantMode = TenantMode.DatabasePerTenant;
    nof.SoftDeleteEnabled = true;
})
.MigrateOnStart();
```

`ef` 就是原生 `DbContextOptionsBuilder<ConfigurationDbContext>`。DbContextCreationContext 提供当前工厂的 scoped 服务、具体 Context 类型、规范化的显式租户和租户模式；每次创建都会执行回调，包括 ITenantDbContextFactory 的显式租户创建。选择连接时使用 creation.TenantId，它可能与 Context.Current 不同。无需连接字符串 Resolver 委托及中间字符串管道，也不会在注册时捕获 scoped 服务实例。

固定连接直接调用原生 Provider：

```csharp
builder.UseDbContext<NotificationsDbContext>((_, ef) =>
    ef.UseSqlite(builder.Configuration.GetConnectionString("notifications")))
    .ConfigureNOFDbContextOptions(nof => nof.TenantMode = TenantMode.SharedDatabase);
```

同一 Context 类型的多个配置回调按注册顺序组合，不同类型之间隔离。NOF 先安装自身 EF 集成默认项，再执行原生回调。NOFDbContextOptions 只保留 TenantMode、SoftDeleteEnabled，默认值不变；主作用域内的 Context 别名和共享事务语义仍保留，不会因选择 Context 而引入第二套业务工作单元。

## 模型与生命周期能力

模型继续直接使用原生 OnModelCreating、EntityTypeBuilder、Property、HasKey/HasIndex、关系、Owned 映射和 IEntityTypeConfiguration。HasSoftDelete 是 NOF 特有的删除策略标注，继续保留；已有 NOF 模型贡献者继续接入按需消息实体和功能模块模型，不增加通用查询/模型/更新外观。

MigrateOnStart 保留为明确启用的宿主/租户迁移编排，执行原生迁移、等待宿主数据库完成并在启动后进行租户迁移，ITenantProvider 提供目录。原生 Database.MigrateAsync/EnsureCreated 和 Context 生命周期 API 可直接使用。本轮不改变 SQLite 初始化及内存连接保活行为。

需要自定义创建生命周期时，仍可 UseDbContext<TContext,TFactory>(nativeCallback) 并继承 NOFDbContextFactory。仅选择连接时直接使用回调，无需派生工厂。

## 破坏性 API 迁移

删除 EF 注册链上的 WithTenantMode、WithSoftDelete、WithConnectionString、WithConnectionStringResolver 和 WithOptions。DbContextConfigurationOptions 替换为仅含 NOF 特有策略的 NOFDbContextOptions；原连接解析上下文和委托替换为原生回调中的 DbContextCreationContext。Provider 与连接配置移入 UseDbContext 回调，NOF 策略移入 ConfigureNOFDbContextOptions，不保留兼容别名。

连接模板由应用明确选择。已有 `{tenantId}` 模板仍可在原生 UseNpgsql/UseSqlite 参数中调用共享的 DbConnectionStringTemplateResolver.ResolveTenantId，不再作为 EF 配置属性。必须跨 Context 保持稳定的随机数据库名或连接实例，应在注册阶段捕获。

本轮不引入数据库结构或数据迁移。保留原 TenantMode、SoftDeleteEnabled 和原生映射，即可维持原列、过滤器、键及删除语义。Sample 的 PostgreSQL 分租户配置、SQLite 共享接收服务和显式启动迁移均保留。
