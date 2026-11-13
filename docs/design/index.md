# Design notes

NOF keeps its runtime surface small by moving repetitive framework work into source generators and explicit architectural building blocks. These notes explain the decisions behind those building blocks.

- [.NET 11 migration](net11-migration.md) — runtime requirements, compatibility, and union type decisions.
- [Result Union migration](../en/design/result-unions.md) ([简体中文](../zh-CN/design/result-unions.md)) — cases, successful messages, JSON, streaming and coordinated upgrades.
- [Application core and adapters](../en/design/0001-application-core-and-adapters.md) ([简体中文](../zh-CN/design/0001-application-core-and-adapters.md)) — responsibilities and phased architecture decision.
- [Request dispatch](request-dispatch.md) — how requests are routed through the framework.
- [Declared expected failures](../en/design/expected-failures.md) ([简体中文](../zh-CN/design/expected-failures.md)) — closed operation outcomes and checked-result diagnostics.
- [Application requests](../en/design/application-requests.md) ([简体中文](../zh-CN/design/application-requests.md)) — explicit registration, execution policies and native adapters.
- [Handler registration](handler-registration.md) — compile-time discovery and dependency registration.
- [Source generators](source-generators.md) — generated infrastructure and diagnostics.
- [Repository and unit of work](repository-uow.md) — persistence boundaries and transaction flow.
- [Value objects](value-object.md) — strongly typed domain values with generated support.
- [Mapping](mapper.md) — mapping conventions and generated implementations.
- [Native hosting](../en/design/native-hosting.md) ([简体中文](../zh-CN/design/native-hosting.md)) — native builders, explicit HTTP composition and lifecycle startup.
- [Public API](public-api.md) — compatibility and surface-area rules.

For types and members, continue to the [API reference](~/api/index.md).

- [Persistence boundaries](../en/design/persistence-boundaries.md) ([简体中文](../zh-CN/design/persistence-boundaries.md)) — aggregate repositories, DTO queries, shared commits and message stores.

- [Unified operations and collections](../en/design/unified-operations.md)
