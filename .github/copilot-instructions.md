# NOF Framework - AI Assistant Instructions

> **See also**: The `.agents/` directory contains organized rules, workflows, and skills for AI agents.

You are working in the **NOF (Neat Opinionated Framework)** repository, a modular .NET framework for building scalable applications with clean architecture, CQRS, and source generators.

## Repository Structure

```text
src/
  NOF.Abstraction/            - shared annotations and ambient event publishing primitives
  NOF.Domain/                 - value objects, failures, domain exceptions, and ID generation
  NOF.Contract/               - application contracts, request/response models, and business outcomes
  NOF.Application/            - RPC servers, handlers, state machines, mapping, and caching abstractions
  NOF.Hosting.Abstraction/    - builder contracts and step contracts
  NOF.Infrastructure/         - builder defaults, EF Core integration, OpenTelemetry, and runtime pipeline
  NOF.UI/                     - reusable UI primitives for Blazor-based clients
  NOF.Test/                   - lightweight test host and scoped test helpers for NOF apps
  Hostings/
    NOF.Hosting.AspNetCore/   - ASP.NET Core host integration and endpoint mapping
    NOF.Hosting.BlazorWebAssembly/ - Blazor WebAssembly host integration
    NOF.Hosting.Console/      - console host integration
    NOF.Hosting.Maui/         - MAUI host integration
  Extensions/
    NOF.Contract.Extension.Authentication/
    NOF.Hosting.Extension.Authentication/
    NOF.Infrastructure.Extension.Authentication/
  Infrastructures/
    NOF.Infrastructure.RabbitMQ/
    NOF.Infrastructure.StackExchangeRedis/
sample/                       - runnable sample app demonstrating current usage
tests/                        - unit and integration tests
```

Follow the pinned [shared standards](../.standards/docs/en/README.md), the root
[AGENTS.md](../AGENTS.md), and the [Result migration guide](../docs/en/design/result-unions.md).
Use lowercase issue-linked topic branches and submit changes to `main` for required independent review.

## Key Patterns

- Results are native `Result(Success, Failure)` / `Result<T>(T, Failure)` unions. Match T directly. `Success<T>` optionally carries data and Message; Failure is shared in NOF.Abstraction. Streaming uses `Result<IAsyncEnumerable<T>>` and paging uses `Result<Page<T>>`.
- Application contracts are declared as `IApplicationService` interfaces with one request parameter and a declared business response per method. DTOs, nullable values and business unions are supported; HTTP routes belong exclusively in native IHttpEndpointModule implementations; legacy HTTP RPC adapters and annotations are deleted.
- Application implementations use `ApplicationService<TService>` and generated nested handler base classes.
- `AddApplicationService<T>()` registers a service container; application parts automatically register operation implementations. During `BuildAsync()`, registered IHttpEndpointModule instances map explicit native routes. Contracts carry no HTTP declarations.
- `NOFWebApplicationBuilder.Create(args)` always registers JSON, CORS, and OpenAPI services.
- `app.MapOpenApi()` is an explicit host choice.
- EF Core registration uses `UseDbContext<TDbContext>()` plus `WithTenantMode(...)`, `WithConnectionString(...)`, `WithOptions(...)`, and optional `MigrateOnInitialize()`.
- Persist application data through `DbContext` / `NOFDbContext` and call `SaveChangesAsync()`; there is no public `_uow` abstraction.
- `Registry` is a first-class builder property; do not describe or implement it as hidden `Properties` state.
- Auto-injected services are recorded as native `ServiceDescriptor` entries in `Registry.AutoInjectRegistry`.
- `TypeResolver` is a DI singleton; do not reintroduce `TypeRegistry` or other process-wide type maps.
- Mapping is expression-based; `ProjectTo<TDestination>()` uses the async-flow ambient `Mapper`. `Mapper`, `IdGenerator`, and `EventPublisher` retain explicit alternatives for testability.
- Avoid reintroducing process-wide mutable `static` state when a builder-level or DI singleton service can hold the same data.
- Central package versions live in `Directory.Packages.props`.

## Build and Validation

```bash
dotnet restore
dotnet build --configuration Release
dotnet test
dotnet format --verify-no-changes
```

## Documentation Sync

When framework behavior changes, check all of the following and keep them consistent with `src/`, `tests/`, and the sample app:

- `README.md`
- `docs/`
- `.agents/`
- `.github/`
- sample usage in `sample/`
