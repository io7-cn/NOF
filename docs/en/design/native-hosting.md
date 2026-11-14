# Native host integration

[简体中文](../../zh-CN/design/native-hosting.md) · [Development](../README.md)

NOF extends native builders instead of providing another host abstraction. Use
WebApplicationBuilder, HostApplicationBuilder, WebAssemblyHostBuilder or MauiAppBuilder
directly. Build is synchronous composition; starting and stopping use the platform lifecycle.
Registration never starts background services or performs database I/O.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddNOF();
builder.AddApplicationPart(typeof(NodeService).Assembly);
builder.AddApplicationService<NodeService>();

var app = builder.Build();
app.UseNOF();
app.MapNodeEndpoints();
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
```

AddNOF on a web builder registers default server infrastructure and HTTP adapters. On a
native worker builder it registers server infrastructure. Registration is idempotent.
Application parts are explicit; both builder.AddApplicationPart and
services.AddApplicationPart apply an assembly's generated registrations once per collection.
There is no caller-assembly discovery. Add the assemblies that own operation implementations and generated registrations. Ordinary HTTP mapping extensions do not require application-part registration.

UseNOF binds and restores scoped ambient conveniences for each native HTTP request; ordinary DI scopes need no activation protocol. See [ambient binding](ambient-binding.md). Call ordinary MapFeatureEndpoints extensions, native MapHealthChecks and MapOidcServer explicitly; no endpoint discovery or DI module registration remains. AddNOF includes ProblemDetails, JSON and OpenAPI services, while MapOpenApi is a host choice. Native authentication, exception handling, antiforgery and protocols remain host responsibilities. See [HTTP adaptation](http-adaptation.md).

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddNOF();
builder.AddApplicationPart(typeof(InvoiceHandler).Assembly);
using var host = builder.Build();
await host.RunAsync();
```

No NOF.Hosting.Console package is needed; reference NOF.Infrastructure for the worker defaults.
NOF.Hosting remains a small integration package, not a host implementation.

## Startup and shutdown

MigrateOnStart registers one native IHostedLifecycleService. Its StartingAsync completes
all configured host database migrations before any IHostedService.StartAsync, including
HTTP and message consumers. Failure or cancellation aborts startup; Build alone does not
migrate. Tenant database migration remains background work after ApplicationStarted.
Scopes, authorized tenant context restoration and cancellation propagation are preserved.

OIDC client bootstrap is an IHostedService.StartAsync task, following the migration phase.
Use the native default sequential service startup when bootstrap must finish before later
services and HTTP begin; opting into concurrent StartAsync removes that ordering guarantee.
Database migration still precedes all StartAsync calls under either setting. NOF does not
silently override HostOptions or reintroduce an initialization ordering engine.
Custom startup tasks use native hosted services and explicit dependencies.

## Deployment identity

DeploymentOptions replaces extension state attached to IHostEnvironment. Configure it
through services.Configure<DeploymentOptions>. Existing ServiceName, ServiceId and InstanceId
configuration keys are retained; ServiceName defaults to the native ApplicationName.
PrimaryNode can explicitly select maintenance leadership; otherwise instance one is primary.
Snowflake IDs, message route names, order keys and diagnostic tags read the same per-host options.
The native environment registration and its configuration/logging lifetime remain untouched.

## Client platforms and tests

WebAssemblyHostBuilder.CreateDefault(args).AddNOF() and MauiApp.CreateBuilder().AddNOF()
register client UI services only. Use platform Build and lifecycle; neither client creates
a parallel IHost, simulates IHostedService loops or starts server inbox/outbox services.
Use services.AddApplicationPart when a client needs generated registrations.

Tests use Host.CreateApplicationBuilder().AddNOFTest(), then BuildTestHost() for the
application test helpers. BuildTestHost does not start the host. Tests requiring migration,
bootstrap or workers explicitly await host.Host.StartAsync(). Prefer the native ASP.NET
Core test server for HTTP integration tests.

## Breaking migration

Delete NOFWebApplicationBuilder, NOFConsoleHostBuilder, NOFWebAssemblyHostBuilder,
NOFMauiAppBuilder, NOFWebAssemblyHost, NOFMauiApp and NOFTestAppBuilder usage. Use native
builders plus registration extensions. Remove BuildNOFAsync, InitializeNOFAsync and
IApplicationInitializationStep implementations. Initialization Compare/topology APIs,
DependencyGraph, the NOF401 analyzer/code fix and NOF.Hosting.SourceGenerator are removed.
Replace MigrateOnInitialize with MigrateOnStart and move I/O assertions from build to startup.
Replace environment extension properties and custom primary predicates with DeploymentOptions.
Map HTTP modules explicitly and register their application parts. Upgrade all packages and hosts
together; rollback restores the previous coherent package/configuration set.

Reliable messaging is assembled only by AddOutbox/AddInbox (enabled by transport endpoint configuration); see [worker capability configuration](transactional-outbox.md#capability-assembly-and-worker-hosts).
