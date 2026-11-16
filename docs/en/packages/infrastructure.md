# NOF.Infrastructure

[简体中文](../../zh-CN/packages/infrastructure.md)

Execution, optional providers and transactional Outbox.

AddNOF selects execution and common policy. AddCacheService, AddObjectStorage, AddBackplane and AddIdGenerator select optional providers explicitly. Memory state belongs to one host. AddOutbox enables outgoing staging/store/workers; message endpoint mapping enables no persistence. Business owns consumer idempotency and commits. InMemoryInvocationPipeline applies effective Global/Service/UseCase filters and validates allowed outcome types. The native host owns authentication and telemetry exporters.

## Installation

```sh
dotnet add package NOF.Infrastructure
```

## Usage and boundaries

- [host-composition](../design/host-composition.md)
- [invocation-extensions](../design/invocation-extensions.md)
- [transactional-outbox](../design/transactional-outbox.md)
- [object-storage](../design/object-storage.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
