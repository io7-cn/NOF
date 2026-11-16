# NOF framework

[简体中文](../zh-CN/overview.md)

NOF (Neat Opinionated Framework) is a .NET business application framework with Domain, Contract, Application, Infrastructure and native host boundaries. Runtime projects target .NET 11/C# 15; source generators remain netstandard2.0. The next NOF major version is 12; published versions remain immutable.

## Capabilities

- Contract-first or standalone application operations with automatic handler registration.
- Native C# unions for declared failures, generated in Application; no generic Contract Result or generated client.
- Uniform authorization, tenant checks and host-selected invocation filters before business execution.
- Explicit native HTTP endpoint modules, resource/ProblemDetails mapping, OpenAPI and server-sent events (SSE).
- Producer transactional Outbox; direct RabbitMQ/Memory consumption with business-owned idempotency.
- Domain value objects, read-only collections, expression mapping and scoped unit-of-work ports.
- Optional cache, object storage, backplane broadcast and ID providers, selected explicitly in the host.
- Native ASP.NET Core identity integration and host-owned OpenTelemetry exporters.
- Blazor permission/browser services, MAUI/WebAssembly composition and isolated test hosts.

## Start with the maintained Sample

The [capability workbench](design/sample-workbench.md) runs two NOF services, RabbitMQ, PostgreSQL, Garnet and an external Keycloak identity provider. It exposes visible controls for the major capabilities; it sends no real email. See its guide for startup, local accounts, routes and upgrade precautions.

For your own application, declare service models in Contract, implement generated operations in Application, implement owned persistence/transport ports in infrastructure and explicitly compose them in the host. Use the [complete configuration CRUD example](design/configuration-rest.md) and [package guide](packages/index.md) rather than copying retired registration APIs.

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();
// After native Build/UseNOF, the application's endpoint module maps its operations.
```

The names in this short example belong to your application. Full runnable composition lives in the Sample. No transport is implied by a Contract declaration.

## Development and compatibility

Use the SDK/workload set in global.json and centrally managed versions in Directory.Packages.props. Native persistence retains the compatible EF/provider pair selected there. Follow [contributing](contributing.md), [code organization](development/code-organization.md), [architecture](design/index.md) and [security reporting](security.md). Public payload names and serialization are compatibility boundaries, even when source files move. Package installation uses the selected published version, not an assumption that this unreleased source is already available on NuGet.

NOF is licensed under [Apache-2.0](../../LICENSE).
