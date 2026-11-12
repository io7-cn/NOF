# NOF Framework

[Development standards (English)](docs/en/README.md) · [开发规范（简体中文）](docs/zh-CN/README.md)

[![CI](https://github.com/XmmShp/NOF/actions/workflows/ci.yml/badge.svg)](https://github.com/XmmShp/NOF/actions/workflows/ci.yml)
[![CD](https://github.com/XmmShp/NOF/actions/workflows/cd.yml/badge.svg)](https://github.com/XmmShp/NOF/actions/workflows/cd.yml)

**NOF** (short for **N**eat **O**pinionated **F**ramework) is a modular, convention-driven application framework for .NET that embraces clean architecture principles. It provides a structured approach to building scalable applications with built-in support for CQRS, transactional outbox, state machines, multi-tenancy, and observability.

## Features

The next major version targets **.NET 11 and C# 15**. See the
[migration and union type guide](docs/design/net11-migration.md) for requirements,
breaking changes, and the persistence dependency boundary. NOF 11.0.0 remains the
.NET 10 release baseline.

- **Clean Architecture** - layered packages (`Domain`, `Contract`, `Application`, `Infrastructure`) enforce separation of concerns
- **CQRS & Messaging** - first-class `IApplicationService`, typed command/notification dispatch, handler pipelines, and native HTTP streaming integration
- **Source Generators** - compile-time code generation for application service operations, DI registration, mapping registration, and failure definitions
- **Transactional Outbox** - reliable message delivery with inbox/outbox pattern built into EF Core infrastructure
- **State Machines** - declarative, event-driven state machine builder with persistent context
- **Multi-Tenancy** - tenant-aware persistence and deployment-aware runtime defaults
- **Object Storage** - provider-neutral streaming storage with tenant-aware key isolation
- **Modular Pipeline** - dependency-aware registration and initialization steps
- **OpenTelemetry** - built-in tracing, metrics, and logging integration

## Runtime Model Highlights

- `Registry` is a first-class builder property and owns handler, mapper, RPC, and auto-inject metadata.
- Auto-injected services are stored as native `ServiceDescriptor` entries in `Registry.AutoInjectRegistry`.
- Message metadata is registered through source-generated handler registries, and inbound dispatch resolves a generated per-handler invoker instead of relying on a process-wide static type resolver.
- `Mapper`, `IdGenerator`, and `EventPublisher` expose ambient async-flow convenience APIs while still supporting explicit dependency paths.
- Built-in in-memory cache state is isolated per host through DI singletons rather than process-wide mutable `static` fields.

Application services keep generated Service.Operation overrides and support ordinary DTOs, nullable values and business unions. Local calls need no transport declaration; HTTP routes live in endpoint modules; legacy HTTP RPC adapters and annotations are deleted. See the [architecture decision](docs/en/design/0001-application-core-and-adapters.md) and [migration guide](docs/en/design/application-requests.md).

## Packages

| Package                                                                                                                           | Description                                                                                                             |
| --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| [`NOF.Abstraction`](https://www.nuget.org/packages/NOF.Abstraction)                                                               | Shared annotations, user context, ambient headers, and in-memory event primitives                                       |
| [`NOF.Domain`](https://www.nuget.org/packages/NOF.Domain)                                                                         | Domain primitives - value objects, failures, domain exceptions, and ID generation                                       |
| [`NOF.Contract`](https://www.nuget.org/packages/NOF.Contract)                                                                     | Contract layer - RPC contracts, request/response models, `Result<T>`, `Result<IAsyncEnumerable<T>>`, `Optional<T>` |
| [`NOF.Application`](https://www.nuget.org/packages/NOF.Application)                                                               | Application layer - RPC servers, handlers, state machines, mapping, caching, and object-storage contracts              |
| [`NOF.Hosting`](https://www.nuget.org/packages/NOF.Hosting)                                               | Hosting abstractions - builder contracts, step contracts, dependency ordering, and JWT outbound propagation             |
| [`NOF.Infrastructure`](https://www.nuget.org/packages/NOF.Infrastructure)                                                         | Core infrastructure - runtime defaults, object storage, transactional messaging, OpenTelemetry, auth resource server, and service wiring |
| [`NOF.Infrastructure.AmazonS3`](https://www.nuget.org/packages/NOF.Infrastructure.AmazonS3)                                       | AWS S3 and S3-compatible object-storage rider                                                                           |
| [`NOF.UI`](https://www.nuget.org/packages/NOF.UI)                                                                                 | Reusable UI primitives - authorization components, browser storage, browser info, and Blazor client helpers             |
| [`NOF.Hosting.AspNetCore`](https://www.nuget.org/packages/NOF.Hosting.AspNetCore)                                                 | ASP.NET Core hosting - middleware, OpenAPI registration, service endpoint mapping, JSON configuration, and SSE streaming |
| [`NOF.Hosting.BlazorWebAssembly`](https://www.nuget.org/packages/NOF.Hosting.BlazorWebAssembly)                                   | Blazor WebAssembly hosting - host builder integration for browser apps                                                  |
| [`NOF.Hosting.Console`](https://www.nuget.org/packages/NOF.Hosting.Console)                                                       | Console hosting - Microsoft.Extensions.Hosting integration with the NOF pipeline                                        |
| [`NOF.Hosting.Maui`](https://www.nuget.org/packages/NOF.Hosting.Maui)                                                             | .NET MAUI hosting - MAUI app builder integration for cross-platform applications                                        |
| [`NOF.Hosting.AspNetCore.Extension.OidcServer`](https://www.nuget.org/packages/NOF.Hosting.AspNetCore.Extension.OidcServer) | OIDC service definitions and ASP.NET Core endpoints for the authentication authority                                   |
| [`NOF.Infrastructure.RabbitMQ`](https://www.nuget.org/packages/NOF.Infrastructure.RabbitMQ)                                       | RabbitMQ transport for NOF command and notification dispatch                                                            |
| [`NOF.Infrastructure.StackExchangeRedis`](https://www.nuget.org/packages/NOF.Infrastructure.StackExchangeRedis)                   | Redis-backed cache infrastructure via StackExchange.Redis                                                               |
| [`NOF.Test`](https://www.nuget.org/packages/NOF.Test)                                                                             | Test host helpers for NOF applications                                                                                  |

## Quick Start

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using NOF.Infrastructure.StackExchangeRedis;

var builder = NOFWebApplicationBuilder.Create(args);

builder.AddApplicationPart(typeof(MyAppService).Assembly);

builder.Services.AddRedisCache(builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException("Connection string 'redis' not found."));
builder.Services.AddRabbitMQ(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq");
});

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnInitialize();

// Modules with only IServiceCollection can use services.AddApplicationService<MyAppService>().
builder.AddApplicationService<MyAppService>();

var app = await builder.BuildAsync();

app.MapOpenApi();

await app.RunAsync();
```

In `DatabasePerTenant` mode, NOF deliberately uses one model and one migration chain for both the host database and every tenant database. They therefore have identical schemas. Tables used only by host-level features are still present in tenant databases and may remain empty; accepting this schema superset is the tradeoff for simpler model construction, migrations, deployment orchestration, and persistence-provider behavior.

See the [Result Union migration guide](docs/en/design/result-unions.md) ([简体中文](docs/zh-CN/design/result-unions.md)) for the NOF 12 breaking contract.

## Streaming RPC

NOF RPC contracts can expose server-streaming operations by returning `Result<IAsyncEnumerable<T>>` from the contract surface. Generated server operation entries return `Task<Result<IAsyncEnumerable<T>>>`. Client generation is completely removed; remote HTTP integration is maintained by callers.

```csharp
public record WatchOrdersRequest(Guid CustomerId);
public record OrderEvent(Guid OrderId, string Status);

public interface IOrderService : IApplicationService
{
    Result<IAsyncEnumerable<OrderEvent>> Watch(WatchOrdersRequest request);
}
```

Server invocation stays result-first:

```csharp
var result = await OrderService.Watch.InvokeAsync(services, new WatchOrdersRequest(customerId), Context.Empty, cancellationToken);
if (result is IAsyncEnumerable<OrderEvent> events)
{
    await foreach (var item in events.WithCancellation(cancellationToken))
    {
        Console.WriteLine($"{item.OrderId}: {item.Status}");
    }
}
```

HTTP endpoint modules in `NOF.Hosting.AspNetCore` use native ASP.NET Core server-sent events. HTTP route declarations stay outside Contract; `IRpcResponse` has been removed. See the [complete configuration REST example](docs/en/design/configuration-rest.md) ([简体中文](docs/zh-CN/design/configuration-rest.md)).

## Architecture

```text
NOF.Abstraction                <- Shared annotations, user context, in-memory events
NOF.Domain                     <- Value objects, failures, domain utilities
NOF.Contract                   <- RPC contracts, payload models, business outcomes
NOF.Application                <- RPC servers, handlers, state machines, mapping, caching
NOF.Hosting        <- Host builder and step contracts
NOF.Infrastructure             <- Builder defaults, shared runtime pipeline, and auth resource server
NOF.UI                         <- Shared UI components and browser client primitives
NOF.Test                       <- Test host helpers for application and integration testing
NOF.Hosting.AspNetCore         <- ASP.NET Core host integration
NOF.Hosting.BlazorWebAssembly  <- Blazor WebAssembly host integration
NOF.Hosting.Console            <- Console host integration
NOF.Hosting.Maui               <- .NET MAUI host integration
NOF.Infrastructure.*           <- Messaging and caching providers
```

## Documentation

Visit the [NOF homepage](https://nof.io-vii.com/) for an overview and quick start. Full generated API documentation is available in the [API Reference](https://nof.io-vii.com/api/).

## JSON And AOT

NOF supports both regular JIT execution and Native AOT-oriented setups.

- Framework-managed HTTP JSON paths use `JsonTypeInfo`/`JsonSerializerContext`-based APIs where possible.
- `JsonSerializerOptions.NOF` ships with built-in metadata for NOF's common primitive needs and value-object primitives.
- Application DTOs still need source-generated metadata in AOT scenarios.

Register your app context before the type is first serialized or deserialized:

```csharp
using System.Text.Json;
using MyApp;

JsonSerializerOptions.ConfigureNOFJsonSerializerOptions(options =>
{
    options.TypeInfoResolverChain.Add(MyAppJsonSerializerContext.Default);
});
```

If metadata is missing, NOF now throws a framework-specific error that includes the concrete runtime type name to make AOT failures easier to locate.

## Testing

The test layout is organized by package category:

- Core and host package tests: `tests/NOF.*.Tests`
- Extension package tests: currently colocated with the closest infrastructure or host package tests
- Infrastructure provider tests: `tests/Infrastructures/NOF.*.Tests`
- Shared test utilities: `tests/Common/*`
- Source generator tests are colocated with parent package tests when practical

## Dependency Version Management

This repository uses Central Package Management via `Directory.Packages.props`.

- Root-level shared package versions are defined in `Directory.Packages.props`.
- `sample/Directory.Packages.props` imports the root file and should only declare sample-specific package versions.
- Avoid defining the same `<PackageVersion Include="...">` in both files, or NuGet restore will emit duplicate package version warnings (for example `NU1506`).

## License

This project is licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).
