# Host composition and identity ownership

[简体中文](../../zh-CN/design/host-composition.md) · [Development guide](../README.md)

NOF owns application operations, business authorization, execution context and transactional message semantics. Native hosts own authentication protocols, telemetry and concrete infrastructure selection. AddNOF installs the operation executor, invoker, default invocation engine, instrumentation and generic authorization/tenant filters. It does not select a cache, object store, backplane, ID algorithm, message transport, database, JWT validator or telemetry exporter.

## Explicit capability selection

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.Services.AddCacheService(options => options.KeyPrefix = "app:{tenantId}:")
    .AddMemoryCacheService();
// Or: .AddRedisCache(connectionString);
builder.Services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "objects");
// Or: .AddMemoryObjectStorage() / .AddAmazonS3ObjectStorage(...);
builder.Services.AddBackplane().AddMemoryBackplane();
// Or: .AddRedisBackplane(...) / .AddRabbitMQBackplane();
builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
```

The entry installs common policy; its typed Selector chooses a provider and retains complete method names. Repeated entry calls do not duplicate shared services. Custom implementations can still use ordinary DI. Selecting one capability does not enable another transport or durable worker. Memory messaging is enabled by AddMemoryMessaging; AddInbox/AddOutbox install message-only persistence as needed. Concrete RabbitMQ publishing adapters register their MessageDelivery dependency explicitly. Native EF/NHibernate remain independent provider choices.

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

UseHostTenantAttribute, HostTenantInvocationFilter and ServiceOperationInspector.RequiresHostTenant are removed. No method or magic metadata key bypasses tenant policy or silently changes execution to the host tenant. Select an explicit Context at the entry or install an application-owned filter through Global/Service/UseCase rules. Its position relative to authorization is an explicit application policy. RequireAuthenticatedTenant has no platform-operation exception; a distinct platform policy belongs in application filters. Ordinary missing tenant normalization remains unchanged.

## Per-host JSON options

```csharp
builder.Services.AddNOFJson(options =>
    options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
```

Framework payload serialization uses host-owned IOptions<JsonSerializerOptions>. Host configuration works before or after capability registration, and repeated default registration does not overwrite it. Each JsonObjectSerializer captures an independent snapshot. ASP.NET Core owns HTTP JsonOptions and their native configuration order. Shared immutable source-generated metadata is safe; there is no shared mutable options instance or first-use configuration deadline.

JsonSerializerOptions.NOF and ConfigureNOFJsonSerializerOptions are removed. Standalone code can create independent defaults with JsonSerializerOptions.CreateNOF(). This factory does not configure DI or another host. IObjectSerializer remains a narrow byte/JSON-metadata boundary used by cache and durable payloads, including reflection-disabled execution. Supply generated contexts for application payloads; reflection fallback is only included when the runtime explicitly enables it. Insert generated contexts ahead of any fallback resolver.

## OIDC: use the upstream protocol implementation

NOF no longer maintains an OAuth/OIDC server or a wrapper package around one. All three NOF OidcServer packages, their endpoint interfaces, protocol models, key rotation, token repositories and ORM integrations are removed. Use an existing identity provider or install OpenIddict directly in the host. [OpenIddict](https://documentation.openiddict.com/introduction) supplies protocol handling and native storage while leaving login, consent and application authorization to the host.

The Sample references OpenIddict.Server.AspNetCore and OpenIddict.EntityFrameworkCore directly, and uses JwtBearer for API authentication. It demonstrates authorization code + PKCE, client credentials, refresh, device authorization, discovery/JWKS, introspection/revocation, userinfo and token exchange through the native APIs. Its small Identity module contains demo user/consent decisions and bootstrap only; it is application code, not a new NOF protocol layer. OAuth demo administration and fixed-user approval are development-only. Production hosts must replace demo identity/consent decisions and development credentials with their own sign-in policy and durable signing/encryption credentials.

Identity uses the existing ConfigurationDbContext, connection and migration chain. OpenIddict's four native tables are added alongside application and message tables. Disable business soft-delete conventions for these native entities so upstream revocation/pruning semantics remain intact. Startup database migration completes before demo clients are seeded. DatabasePerTenant hosts share the same schema chain; identity requests use the host context and application-selected tenant routing remains explicit.

Token exchange retains upstream presenter/audience constraints. The demo authorization decision accepts an explicit delegate_to registered exchange client, includes that recipient in the approved resources, and exchanges only scopes shared by subject and actor. The UI passes RecipientClientId when obtaining its user token. This is demo consent policy, not permission to exchange arbitrary stolen tokens. JwtBearer performs offline JWT validation; revocation is observed by introspection, while API acceptance is bounded by access-token lifetime unless the host selects an online validation policy. Sample dynamic client provisioning uses the native application manager; no dynamic-registration protocol endpoint is exposed.

The selected OpenIddict version is verified here on .NET 11 with actual native protocol and SQLite integration tests; the upstream support table currently explicitly lists through .NET 10. Framework reflection-disabled smoke coverage does not claim Native AOT support for every third-party identity provider.

## Migration

Replace root provider registration calls with the corresponding Selector chain, explicitly register required optional capabilities, and move telemetry configuration into the host. Remove AddNOFDomain; update Snowflake imports to NOF.Infrastructure. Audit IdGenerator.Current callers for nullable handling. Replace framework JWT registration with native AddAuthentication/AddJwtBearer and middleware ordering; remove credential Context.Items and host-tenant annotations. Applications requiring special tenant selection own the rule explicitly.

The Sample's 20261114200000_UseOpenIddict migration follows RemoveInboxClassification and only creates native OpenIddict tables/indexes. Existing OAuthClient, PersistedSigningKey and RevokedRefreshToken tables remain untouched outside the active model. Existing business/message payloads, headers, IDs, leases and receipt history remain intact. Do not reinterpret old credential hashes, encryption keys, authorization codes or refresh tokens as OpenIddict records. Stop old binaries, back up each affected database, review native client/permission provisioning and credential rotation, and require re-authentication or an explicitly reviewed transition plan. Down requires restoring matching backups/packages; it does not guess a reverse conversion. No live database upgrade is performed as part of source validation.
