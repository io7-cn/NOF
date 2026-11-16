# NOF.Infrastructure.StackExchangeRedis

[English](../../en/packages/redis.md)

Redis 缓存与瞬态广播 Provider。

分别使用 AddCacheService().AddRedisCache(connectionString) 与 AddBackplane().AddRedisBackplane(connectionString)，它们是独立能力，使用原生 StackExchange.Redis 配置。CacheServiceOptions 可通过 Selector 链配置租户键前缀等策略。Backplane 广播不能代替可靠 Outbox 投递或业务幂等。

## 安装

```sh
dotnet add package NOF.Infrastructure.StackExchangeRedis
```

## 用法与边界

- [host-composition](../design/host-composition.md)
- [execution-context](../design/execution-context.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。

## Selector 示例

```csharp
var connection = builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException("Connection string 'redis' is required.");
builder.Services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "orders:{tenantId}:")
    .AddRedisCache(connection);
builder.Services.AddBackplane().AddRedisBackplane(connection);
```
