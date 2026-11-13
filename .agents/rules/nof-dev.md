---
trigger: always_on
---

# NOF Framework Development Rules

Use this file when contributing to the NOF framework itself.

## Repository Layout

- `src/`: runtime packages, source generators, code fixes, hosting integrations, extensions, and providers.
- `src/Hostings/`: ASP.NET Core, Blazor WebAssembly, Console, and MAUI hosts.
- `src/Infrastructures/`: EF Core, NHibernate, RabbitMQ, and StackExchange.Redis providers.
- `src/Extensions/`: optional feature packages, currently the ASP.NET Core OIDC server.
- `sample/`: runnable multi-project sample and Aspire app host.
- `sample-tests/`: sample integration tests.
- `tests/NOF.*.Tests`: core, hosting, UI, and test-helper tests.
- `tests/Infrastructures/*`: provider-specific tests.
- `tests/Common/SourceGenerator`: shared generator test helpers.
- `docs/`: DocFX content; `website/`: homepage application.

## Tech Stack

- .NET 11 and C# 15 (`extension` blocks are used throughout the repository).
- Central Package Management through root `Directory.Packages.props`; `sample/Directory.Packages.props` imports it for sample-only versions.
- Roslyn incremental generators and analyzers targeting `netstandard2.0`.
- xUnit and Moq.

## Current Runtime Patterns

- Application services: declare IApplicationService contracts and partial ApplicationService<TService> containers; implement generated Service.Operation overrides. DTOs, nullable values and business unions are ordinary responses; IRpcResponse is removed. AddApplicationPart automatically registers scoped operation implementations; register containers with AddApplicationService<TService>(). Operation identity is service contract plus operation, so input/output models may be shared. See docs/en/design/application-requests.md.

- RPC: `IApplicationService`, `ApplicationService<TService>`, generated server operation entries, and `AddApplicationService<TServer>()`.
- Messaging: plain payload types plus `CommandHandler<T>` / `NotificationHandler<T>`; all handler and dispatch APIs receive `Context`.
- In-memory events: `InMemoryEventHandler<T>`, `IEventPublisher`, and optional `PublishAsEvent()` ambient convenience.
- Transactional outbox: asynchronous `DeferSendAsync(...)` / `DeferPublishAsync(...)`, plus ordered variants, followed by `IDbContext.SaveChangesAsync()`.
- Persistence: application-facing `IDbContext`, `IDbContextFactory`, and `IRepository<T>`; EF Core and NHibernate adapters live in provider packages.
- Hosting: use native builders and synchronous Build. AddNOF registers defaults; Web hosts explicitly call UseNOF and MapNOFEndpoints. IApplicationInitializationStep, topology, BuildNOFAsync, NOF host/builder wrappers, NOF.Hosting.Console, NOF.Hosting.SourceGenerator and NOF401 are removed. Database migration uses IHostedLifecycleService.StartingAsync via MigrateOnStart; OIDC client bootstrap uses IHostedService.StartAsync. DeploymentOptions replaces IHostEnvironment extension state. Client AddNOF only registers UI services. See docs/en/design/native-hosting.md.
- Source generation: `[AutoInject]`, `[FailureCase]`, `[Mappable<TSource, TDestination>]`, `[NewableValueObject]`, `IValueObject<T>`, handler registration, and server operation/descriptor generation (no client generation).
- Application parts: `AddApplicationPart(assembly)` runs generated `AssemblyInitializeAttribute` initializers against the current `IServiceCollection`.
- Registries: `EventHandlerRegistry`, `MappingRegistry`, `CommandHandlerRegistry`, `NotificationHandlerRegistry`, and `ApplicationServiceRegistry` are singleton instances stored in DI and freeze on first read. Do not reintroduce a process-wide type registry.
- Ambient helpers: `Mapper`, `IdGenerator`, and `EventPublisher` are scoped through daemon-service resolution; `Context.Current` follows the current async execution flow. Explicit APIs remain the primary runtime contracts.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing belongs in IHttpEndpointModule using native ASP.NET Core binding/TypedResults and HttpContext.InvokeAsync(Service.Operation.Descriptor, request, ct). Modules use AutoInject(Singleton) plus application-part initialization. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application defines IInvocationFilter/InvocationContext/InvocationDelegate and IInvocationPipeline; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily in ascending stable order under the current context; authorization/tenant checks precede handler construction. MessageDelivery directly prepares ID, tenant and producer trace metadata for immediate and outbox sends. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. See docs/en/design/invocation-extensions.md.

## Coding Rules

- Default classes to `public` throughout the repository. Use `internal` only when a compelling, concrete reason requires assembly-only visibility, and document that reason alongside the declaration. Being an implementation detail, a background service, or currently used only inside the repository is not sufficient justification.
- Use file-scoped namespaces, Allman braces, and braces for every control-flow body.
- Add XML documentation for public APIs under `src/`.
- Do not put NuGet versions in individual `.csproj` files.
- Register services directly through `IServiceCollection`; there is no service-registration-step pipeline.
- Preserve Native AOT annotations and analyzer isolation: a generator must not depend on another generator's output being visible as input.

## Build Commands

```bash
dotnet restore NOF.slnx -p:Configuration=Release
dotnet build NOF.slnx --configuration Release --no-restore
dotnet format --no-restore --verify-no-changes --verbosity diagnostic
dotnet test NOF.slnx --configuration Release --no-build --verbosity normal --collect:"XPlat Code Coverage"
```

Build before formatting so source-generator assemblies exist; set the Configuration environment variable to Release for format analysis. The GitHub matrix installs the MAUI workload on Windows before restore. See `workflows/nof-dev/run-ci-locally.md` for the exact sequence.

## Change Checklist

Before considering work done, verify all applicable items:

1. Put runtime and generator tests in the owning package test project; put provider tests under `tests/Infrastructures/` when a dedicated project is warranted.
2. Keep the sample and `sample-tests` compiling when public behavior changes.
3. Update public XML docs, `README.md`, `docs/`, and `.agents/` as applicable.
4. Add new projects to `NOF.slnx`; update CI/CD only when its discovery or build inputs require it.
5. Add dependency versions only to root `Directory.Packages.props`, or to `sample/Directory.Packages.props` for sample-only packages.
6. Run `dotnet format`, review any changes, then run the relevant builds and tests.

Declared expected failures use Contract FailureCase(name), FailureCase(name, errorCode), or FailureCase(name, typeof(CustomError)). The first argument is always a unique valid C# branch name; invalid declarations report NOF220. Default cases expose ErrorCode (defaulting to the name) and Message; custom cases expose typed Error without an imposed code or message. Service methods declare the successful type; generated IService.<Operation>Operation.Result is used by operation overrides, Descriptor and InvokeAsync. Contract operation bases own the canonical Result and sealed failure cases. Application operations inherit them, so handlers use Task<Result> and new Case(...). No CreateCase factories, shared Error wrapper, ExpectedOutcomeAttribute or NOF230/231/232 analyzer remain; native C# union switch-expression exhaustiveness checks apply. Ignoring results and catch-all arms follow ordinary C# semantics. ServiceOperation abstractions live in NOF.Contract. Do not use the removed Domain FailureAttribute, static failure holders, failure.Throw or Failure-accepting exception constructors. Domain returns its own business values without depending on Contract. See docs/en/design/expected-failures.md.

Value objects implement static bool IsValid(T, out string? errorMessage) and optional Normalize. Generated From throws for invalid values; TryFrom returns false with default out for expected invalid input and never catches user-hook exceptions. Of and Validate are removed; legacy Validate or single-parameter IsValid is NOF019. Use TryFrom at application input boundaries to avoid validating twice. See docs/en/design/value-objects.md.

IsValid always has out string? errorMessage. Detailed TryFrom returns a non-null failure message and null on success; From uses the same text with ArgumentException. DomainException/DomainValidationException are removed. Application handlers forward TryFrom messages into FailureCase rather than duplicate wording.
