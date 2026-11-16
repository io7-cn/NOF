# .NET 11 migration and union types

[简体中文](../../zh-CN/design/net11-migration.md)

NOF 12 targets .NET 11 and C# 15. NOF package versions are independent of .NET versions; the existing NOF 11 release remains immutable. The SDK and workload set are pinned in `global.json`. Source generators and code fixes retain `netstandard2.0`; use the Roslyn versions in `Directory.Packages.props`.

## Build and persistence

Install the pinned SDK and the platform workloads required by `NOF.slnx`. Follow [the contributor guide](../contributing.md) for restore, Release build, formatting and tests. Windows builds include MAUI; Linux uses the repository's platform stubs and performs Native AOT validation in CI.

The repository currently pins a compatible EF Core/PostgreSQL provider combination in `Directory.Packages.props`. The runtime target does not require every database dependency to share the .NET major version. Upgrade provider/runtime pairs together and review native migrations before deployment.

## Current union boundaries

| Boundary | Current model |
| --- | --- |
| Declared business failures | Contract methods declare success and `FailureCase`; Application generates the closed `Service.Operation.Result` and failure branches. |
| Authorization | `InboundAuthorizationResult` represents allowed/denied outcomes; policies run before handler construction. |
| Missing data | `Optional<T>` retains explicit presence, including present null, and is not replaced by a failure union. |
| Streaming and paging | `IAsyncEnumerable<T>` and `Page<T>` are business values; HTTP uses native SSE or resource mapping. |
| Protocol identity | External providers and native ASP.NET Core authentication own OIDC/JWT behavior; NOF contains no OAuth protocol result hierarchy. |

Generic Contract `Result`/`Result<T>`, Success wrappers and client generation are removed. Native C# union switch expressions provide exhaustive matching; NOF adds no checked-result analyzer. Default/uninitialized unions are rejected at the invocation boundary. Complete invocation may additionally return effective filter failures, so use `InvocationResult.Value` when inspecting a boxed business result.

## Coordinated upgrade

Update Contract, Application, adapters and consumers together. Preserve payload type identities and stored message headers; public payload renames need an explicit durable-key migration. Replace old Result factories with business values or generated cases, and map HTTP responses by concrete type. Back up databases and review the documented Outbox/legacy Inbox and identity transitions. Roll back using matching binaries, callers and database backups, not mixed generations.

See [operation outcomes](result-unions.md), [expected failures](expected-failures.md), [Outbox migration](transactional-outbox.md), [native EF configuration](efcore-configuration.md) and [host composition](host-composition.md).
