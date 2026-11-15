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

Native EF configuration uses UseDbContext<T>((creation, ef) => ef.UseNpgsql(...)).ConfigureNOFDbContextOptions(nof => ...).MigrateOnStart(). NOFDbContextOptions contains only TenantMode and SoftDeleteEnabled; provider, connections, logging, interceptors and retries stay on the native typed EF builder. Register ITenantProvider for durable database-per-tenant migration and message polling. Native startup migration precedes hosted-service client bootstrap; tenant migration failures require explicit recovery. Use creation.Services and creation.TenantId in the native callback for application tenant routing. NOFDbContext applies native model configuration, value-object conversions, owned collection conventions and optional soft deletion.

Messaging: endpoints directly execute the mapped OperationHandler in isolated scopes through OperationExecutor, restoring Context before constructors and preserving authorization/filters. No Inbox/store/entity/receipt registry or consumer worker ships. Business owns idempotency, saves and transactions; native adapters own ACK/NACK, retry/dead-letter and concurrency. RabbitMQ ACK follows completed execution, not acceptance. Memory awaits handlers and propagates failures without process durability. MapQueue/AddMemoryMessaging never enable persistence. AddOutbox explicitly enables producer staging/workers; configure flat OutboxOptions and retain generated OutboxHandler<T> discovery. Resolve legacy Inbox Pending/Failed work with old binaries or explicit reviewed export/replay before guarded RemoveInbox; preserve required historical receipts via backup. See docs/en/design/transactional-outbox.md.

Authentication is native AddAuthentication().AddJwtBearer(...), UseAuthentication/UseAuthorization and endpoint RequireAuthorization. NOF only copies verified HttpContext.User. Local calls isolate the scoped principal; messages carry no durable user identity. Special tenant selection is application policy; no host-tenant annotation exists.

Use an external identity provider or OpenIddict directly; NOF owns no OIDC protocol or persistence package. Sample Identity uses the existing ConfigurationDbContext and migration chain, alongside message/business tables. Native identity records opt out of business soft deletion. Login/consent/client management and credentials belong to the host. Do not convert old tokens/hashes automatically; see docs/en/design/host-composition.md for migration and production constraints.

JSON payloads use AddNOFJson with per-host IOptions<JsonSerializerOptions>; HTTP uses ConfigureHttpJsonOptions. Register generated contexts at index zero for reflection-disabled execution. Standalone CreateNOF() returns independent options. The host configures native OpenTelemetry sources/meters/exporters.
