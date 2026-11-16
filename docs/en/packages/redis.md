# NOF.Infrastructure.StackExchangeRedis

[简体中文](../../zh-CN/packages/redis.md)

Redis cache and transient broadcast providers.

Select AddCacheService().AddRedisCache(connectionString) or AddBackplane().AddRedisBackplane(connectionString). They are independent capabilities using native StackExchange.Redis settings. Configure CacheServiceOptions through the selector chain, including tenant key prefixes. Backplane broadcasts do not replace durable Outbox delivery or business idempotency.

## Installation

```sh
dotnet add package NOF.Infrastructure.StackExchangeRedis
```

## Usage and boundaries

- [host-composition](../design/host-composition.md)
- [execution-context](../design/execution-context.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).

## Selector example

```csharp
var connection = builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException("Connection string 'redis' is required.");
builder.Services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "orders:{tenantId}:")
    .AddRedisCache(connection);
builder.Services.AddBackplane().AddRedisBackplane(connection);
```
