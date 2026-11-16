# NOF.Test

[简体中文](../../zh-CN/packages/testing.md)

Native test hosts with explicit scope and invocation helpers.

Host.CreateApplicationBuilder().AddNOFTest selects isolated memory capabilities. Add each application part and service, then BuildTestHost. Build does not start hosted services: start the native host explicitly when testing migration or workers. NOFTestScope configures tenant, verified user, tracing and scalar Context data, binds ambient accessors and resolves dependencies. CallAsync awaits generated operation invocation; no generated local client is involved. Explicitly enable Outbox or native database stores when a test needs them.

## Installation

```sh
dotnet add package NOF.Test
```

## Usage and boundaries

- [application-requests](../design/application-requests.md)
- [ambient-binding](../design/ambient-binding.md)
- [persistence-boundaries](../design/persistence-boundaries.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
