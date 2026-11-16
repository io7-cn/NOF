# NOF.Application

[简体中文](../../zh-CN/packages/application.md)

Operations, business ports and expression mapping.

ApplicationService<TService> containers expose generated operation bases; concrete OperationHandler classes also support internal reactions without a public Contract method. Handlers receive InvocationContext and CancellationToken. Complete invocation returns object because enabled filters may return additional typed failures. Application owns aggregate/query collaboration, IUnitOfWork, IOutbox, ICacheService and IObjectStorage contracts, while native providers stay in infrastructure. Mapping uses one registered expression for both query projection and in-memory conversion.

## Installation

```sh
dotnet add package NOF.Application
```

## Usage and boundaries

- [application-requests](../design/application-requests.md)
- [mapper](../design/mapper.md)
- [persistence-boundaries](../design/persistence-boundaries.md)
- [transactional-outbox](../design/transactional-outbox.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
