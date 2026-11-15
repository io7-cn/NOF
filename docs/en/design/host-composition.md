# Host composition and identity ownership

[简体中文](../../zh-CN/design/host-composition.md) · [Development guide](../README.md)

NOF owns application operations, business authorization, execution context and transactional message semantics. Native hosts own authentication protocols, telemetry and concrete infrastructure selection. AddNOF installs the unified operation executor, default invocation engine, instrumentation and generic authorization/tenant filters. It does not select a cache, object store, backplane, ID algorithm, message transport, database, JWT validator or telemetry exporter.

The HTTP AddNOF overload also registers its adapter prerequisites: JSON, ProblemDetails and OpenAPI. Hosts do not repeat AddProblemDetails(); they may configure native ProblemDetailsOptions when customization is needed.

## Explicit capability selection

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.Services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "app:{tenantId}:")
    .AddMemoryCacheService();
// Or: .AddRedisCache(connectionString);
builder.Services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "objects");
// Or: .AddMemoryObjectStorage() / .AddAmazonS3ObjectStorage(...);
builder.Services.AddBackplane().AddMemoryBackplane();
// Or: .AddRedisBackplane(...) / .AddRabbitMQBackplane();
builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
```

Message endpoints execute operations directly in an isolated scope with restored Context and the normal policy/filter pipeline. NOF owns no Inbox storage, deduplication or consumer retry scheduler. Business code owns idempotency and transaction/save boundaries; RabbitMQ ACK follows operation completion. Native adapters own retries and dead-letter policy. Memory calls await execution and propagate failures. AddOutbox alone enables durable outgoing intents and workers; queue mapping enables no persistence. See [direct endpoints and migration](transactional-outbox.md).

CacheServiceSelector.ConfigureCacheServiceOptions appends a native CacheServiceOptions callback and returns the same selector. It works before or after provider selection and supports repeated calls in registration order; no cache provider is replaced by configuring options.

SnowflakeIdGenerator and SnowflakeIdGeneratorOptions live in Infrastructure. Domain keeps IIdGenerator and the nullable IdGenerator.Current convenience accessor. Missing ambient registration returns null; the caller decides whether to skip, use an alternative, or throw. Generated New() requires an ID and reports the missing provider at that call site; New(IIdGenerator) remains explicit. AddNOFDomain is removed because Domain has no runtime registration responsibilities. AddNOFTest explicitly selects memory cache/object storage and Snowflake for its test conveniences.

NOF continues to emit ActivitySource/Meter instrumentation. Configure native AddOpenTelemetry, sources/meters and exporters in the host. The framework never installs logging exporters or reads OTEL_EXPORTER_OTLP_ENDPOINT to activate one. The Sample contains an explicit native setup.

## Native HTTP identity

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://identity.example.com/";
        options.Audience = "orders";
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseNOF();
app.MapGet<OrderService.Read>("/orders/{id}").RequireAuthorization();
```

JwtBearer performs issuer/audience/lifetime/signature validation, metadata discovery and signing-key refresh. Native endpoint authorization owns challenge and forbid responses. HTTP adapters reset the scoped IUserContext and clone HttpContext.User; they do not interpret raw Authorization headers or Context.Items as credentials. Without a native scheme, NOF business denials still map to ordinary Problem responses but no synthetic WWW-Authenticate header is created.

Local calls copy the caller's scoped identity into an isolated scope. Durable messages do not persist user identities. Generic authorization and tenant trust checks continue to run for every origin, before handler construction. Hosts must establish trusted identities at their own boundaries.

UseHostTenantAttribute, HostTenantInvocationFilter and OperationInspector.RequiresHostTenant are removed. No method or magic metadata key bypasses tenant policy or silently changes execution to the host tenant. Select an explicit Context at the entry or install an application-owned filter through Global/Service/UseCase rules. Its position relative to authorization is an explicit application policy. RequireAuthenticatedTenant has no platform-operation exception; a distinct platform policy belongs in application filters. Ordinary missing tenant normalization remains unchanged.

## Per-host JSON options

```csharp
builder.Services.AddNOFJson(options =>
    options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
```

Framework payload serialization uses host-owned IOptions<JsonSerializerOptions>. Host configuration works before or after capability registration, and repeated default registration does not overwrite it. Each JsonObjectSerializer captures an independent snapshot. ASP.NET Core owns HTTP JsonOptions and their native configuration order. Shared immutable source-generated metadata is safe; there is no shared mutable options instance or first-use configuration deadline.

JsonSerializerOptions.NOF and ConfigureNOFJsonSerializerOptions are removed. Standalone code can create independent defaults with JsonSerializerOptions.CreateNOF(). This factory does not configure DI or another host. IObjectSerializer remains a narrow byte/JSON-metadata boundary used by cache and durable payloads, including reflection-disabled execution. Supply generated contexts for application payloads; reflection fallback is only included when the runtime explicitly enables it. Insert generated contexts ahead of any fallback resolver.

## OIDC: consume an external identity provider

NOF supplies business authorization, not identity protocols or user storage. The Sample AppHost runs an official Keycloak container and imports `Realms/nof-realm.json`. The application uses native ASP.NET Core Cookie/OpenIdConnect for browser sign-in and JwtBearer for bearer API calls. Issuer, audience, signature and lifetime validation belong to these native handlers. HTTPS metadata is required outside Development.

Keycloak issues the `entitlements` permission claims and `nof.tenant_id` tenant claim. NOF evaluates the Contract's declarations against the verified principal; UI AuthGuard checks permission identifiers directly. No permission catalog is required in the framework. Identity-provider accounts, consent, sessions, credentials and protocol upgrades remain provider responsibilities. See [the capability workbench](sample-workbench.md) for local accounts and permission scenarios.

The application no longer references OpenIddict or maps an authorization/token/userinfo server. The previous protocol administration and OAuth-chain pages are removed. An application that needs a different provider configures its own Authority, ClientId, ClientSecret and API audience through native host APIs. The checked-in development realm/client credentials are local demonstration data, never deployment credentials.

## Migration

Replace root provider registration calls with the corresponding Selector chain, explicitly register required optional capabilities, and move telemetry configuration into the host. Remove AddNOFDomain; update Snowflake imports to NOF.Infrastructure. Audit IdGenerator.Current callers for nullable handling. Replace framework JWT registration with native AddAuthentication/AddJwtBearer and middleware ordering; remove credential Context.Items and host-tenant annotations. Applications requiring special tenant selection own the rule explicitly.

The historical `20261114200000_UseOpenIddict` migration remains in the chain. `20261117000000_ExternalIdentityProvider` removes its entities from the active EF model without dropping physical identity tables or altering stored credentials. Existing business/Outbox records remain intact. Back up databases, review provider account/permission provisioning and require a fresh sign-in; no credential/token conversion is attempted. Any later cleanup of obsolete identity tables requires a separate reviewed data-retention decision. Running the local AppHost applies development database migrations through MigrateOnStart.

Permission checking uses NOF.Abstraction; Application resolves Contract declarations and UI needs no Contract reference. AddNOF/AddNOFUI install the default checker and claim-grant provider; application-owned IPermissionGrantProvider implementations query current grants without registering a framework permission dictionary. Contract interfaces/methods are the only declaration source; UI checks IDs directly. See [authorization](authorization.md).

Outbox persists every JSON-scalar Context item and prepares complete immutable delivery Headers; fresh attempt scopes bind the restored Context before handler construction. Native clients forward OutboxDeliveryContext.Headers directly; MessageDelivery and its registrations are removed.

See [JSON context and automatic durable delivery](execution-context.md) for the breaking Context API, ready-to-send headers and fluent registration.
