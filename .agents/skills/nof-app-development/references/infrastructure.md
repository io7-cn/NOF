# Native infrastructure integration

Business handlers depend on aggregate repositories, DTO query ports, IUnitOfWork, ICacheService and application message interfaces. Native DbContext/ISession remain in infrastructure. See docs/en/design/persistence-boundaries.md.

AddNOF selects execution only. Concrete capabilities use typed selectors:

```csharp
builder.Services.AddCacheService().AddMemoryCacheService();
builder.Services.AddCacheService().AddRedisCache(connectionString);
builder.Services.AddObjectStorage().AddFileSystemObjectStorage();
builder.Services.AddBackplane().AddMemoryBackplane();
builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
```

Choose a single cache provider; later explicit selection replaces the rider. Root entries install common policy, providers remain optional packages. ID implementations/options live in Infrastructure; Domain needs no registration. IdGenerator.Current is nullable. New() requires an ambient provider; New(IIdGenerator) is explicit.

Native EF configuration uses UseDbContext<T>().WithTenantMode(...).WithConnectionString(...).WithOptions((options, connection) => options.UseNpgsql(connection)).MigrateOnStart(). Register ITenantProvider for durable database-per-tenant migration and message polling. Native startup migration precedes hosted-service client bootstrap; tenant migration failures require explicit recovery. Use WithConnectionStringResolver for application tenant routing. NOFDbContext applies native model configuration, value-object conversions, owned collection conventions and optional soft deletion.

RabbitMQ uses AddRabbitMQ with native ConnectionFactory and repeatable ConfigureRabbitMQ topology/queue bindings. Mapping queues enables Inbox; topology-only setup does not. AddOutbox stages generic intents; infrastructure handlers select destinations. Native publishing adapters explicitly register MessageDelivery and the serializer they use. Native connections are shared, channels caller-owned. See docs/en/design/rabbitmq-sample.md and transactional-outbox.md.

Authentication is native AddAuthentication().AddJwtBearer(...), UseAuthentication/UseAuthorization and endpoint RequireAuthorization. NOF only copies verified HttpContext.User. Local calls isolate the scoped principal; messages carry no durable user identity. Special tenant selection is application policy; no host-tenant annotation exists.

Use an external identity provider or OpenIddict directly; NOF owns no OIDC protocol or persistence package. Sample Identity uses the existing ConfigurationDbContext and migration chain, alongside message/business tables. Native identity records opt out of business soft deletion. Login/consent/client management and credentials belong to the host. Do not convert old tokens/hashes automatically; see docs/en/design/host-composition.md for migration and production constraints.

JSON payloads use AddNOFJson with per-host IOptions<JsonSerializerOptions>; HTTP uses ConfigureHttpJsonOptions. Register generated contexts at index zero for reflection-disabled execution. Standalone CreateNOF() returns independent options. The host configures native OpenTelemetry sources/meters/exporters.
