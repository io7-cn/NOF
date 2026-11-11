# Design notes

NOF keeps its runtime surface small by moving repetitive framework work into source generators and explicit architectural building blocks. These notes explain the decisions behind those building blocks.

- [.NET 11 migration](net11-migration.md) — runtime requirements, compatibility, and union type decisions.
- [Result Union migration](../en/design/result-unions.md) ([简体中文](../zh-CN/design/result-unions.md)) — cases, successful messages, JSON, streaming and coordinated upgrades.
- [Request dispatch](request-dispatch.md) — how requests are routed through the framework.
- [Handler registration](handler-registration.md) — compile-time discovery and dependency registration.
- [Source generators](source-generators.md) — generated infrastructure and diagnostics.
- [Repository and unit of work](repository-uow.md) — persistence boundaries and transaction flow.
- [Value objects](value-object.md) — strongly typed domain values with generated support.
- [Mapping](mapper.md) — mapping conventions and generated implementations.
- [Steps](step.md) — composable application pipelines.
- [Public API](public-api.md) — compatibility and surface-area rules.

For types and members, continue to the [API reference](~/api/index.md).
