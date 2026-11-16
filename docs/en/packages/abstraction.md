# NOF.Abstraction

[简体中文](../../zh-CN/packages/abstraction.md)

Shared context, identity, permission checks and local events.

Context, immutable permission policies, IUserContext, IEventPublisher and LocalEventHandler<T> form the shared layer. AddNOFAbstraction installs container-local defaults. Managed entries bind ambient conveniences; ordinary dependency-injection scopes need no activation. Prefer explicit async event publishing when handlers perform asynchronous work. Configure JSON metadata per host; JsonSerializerOptions.CreateNOF returns a fresh standalone instance. Failure carries Title/Detail, and expected application failures use generated cases.

## Installation

```sh
dotnet add package NOF.Abstraction
```

## Usage and boundaries

- [authorization](../design/authorization.md)
- [execution-context](../design/execution-context.md)
- [ambient-binding](../design/ambient-binding.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
