# ADR 0001: Application execution core and native adapters

[简体中文](../../zh-CN/design/0001-application-core-and-adapters.md)

- Status: Accepted; current implementation described below.
- Decision date: 2026-11-11. Reviewed: 2026-11-16.
- Owner: NOF maintainers (XmmShp).
- Tracking: [issue #4](https://github.com/io7-cn/NOF/issues/4); prerequisite [.NET migration PR #3](https://github.com/io7-cn/NOF/pull/3).

## Context and options

NOF supports HTTP, local invocation, queue consumers and background work. An RPC-centered core forced transport outcomes and duplicate middleware into business code. The alternatives were to retain that core, place REST wrappers around it, or use a typed application execution core with native adapters. The third option is accepted: keep the familiar service operation override model while moving protocol decisions to their owning hosts.

## Decision

Contract contains synchronous service declarations, request/response models, FailureCase and permission attributes. It generates no code and needs no partial interfaces. Application generates Service.Operation bases, closed Result/failure branches, descriptors and registrations. Operation identity is service plus method, allowing shared inputs/outputs. Standalone OperationHandler classes express internal reactions without exposing a Contract method.

Handlers return business values or declared failures. Native union switch expressions provide exhaustiveness checks. Complete invocation can also return effective host-filter failures; the executor validates permitted outcomes. Technical exceptions and cancellation propagate. Domain keeps invariants and business values without transport/Contract dependencies.

Infrastructure supplies the optional invocation pipeline and Global/Service/UseCase filter plan. Authorization and tenant checks precede handler construction. Hosts choose additional validation and transaction filters. Consumer commits and idempotency remain application responsibilities; neither an executor nor a transport mapping silently saves business state.

| Responsibility | Owner |
| --- | --- |
| Routing, binding, status/body/headers, ProblemDetails, SSE and OpenAPI | Native HTTP endpoint modules and ASP.NET Core |
| Business operation identity, typed outcomes and registrations | Application and its generators |
| Identity protocols, token verification, host lifetime and telemetry exporters | Established providers and native host APIs |
| Permission declarations / current grants / UI checks | Contract / Abstraction grant provider / explicit UI policy |
| Aggregate write and DTO query ports, unit-of-work contract | Domain/Application; native persistence implementations stay in infrastructure |
| Reliable outgoing intents | Explicit AddOutbox and typed infrastructure Outbox handlers |
| Queue/exchange topology, retry/dead-letter and acknowledgement | Native transport configuration; NOF maps operations to queues only |
| Consumer idempotency and side-effect atomicity | Business constraints, transactions or provider idempotency |
| Cache, storage, broadcast and ID algorithms | Explicit optional providers |

HTTP response configuration replaces one whole exact-type mapping; a status-code convenience form remains. Special representations use ordinary C#. No HttpEndpoint Contract annotations, legacy RPC adapters, named response variants or client generators remain. Runtime OpenAPI reads final metadata without executing user presentation callbacks.

Outbox snapshots the complete scalar JSON Context and explicit headers with stable identity/trace. Native publishers forward ready-to-send headers. Consumers restore context and await the mapped operation in a fresh scope. There is no framework Inbox, receipt database, ordered-delivery layer or MessageBinding abstraction. Native ACK follows completed execution, including the business commit chosen by the handler.

## Consequences and migration

The public framework surface is smaller and business code is less transport-dependent. Hosts explicitly select providers and own endpoint/topology modules and remote integrations. This requires native integration testing and deliberate consumer idempotency; it does not claim exactly-once external effects. UI permissions never replace server enforcement, and durable messages do not serialize a live principal or dependency-injection scope.

Update Contract, Application, native adapters and callers together. Preserve payload names and wire data unless a reviewed migration explicitly changes them. Resolve legacy Inbox work before removal, back up affected databases and review native schema/identity transitions. Restore matching binaries and backups for rollback. Merge only through required checks and independent approvals; this decision does not authorize a release.

## Maintained implementation guides

- [Application operations](application-requests.md) and [standalone operations](unified-operations.md)
- [Contract boundaries](contract-boundaries.md) and [expected failures](expected-failures.md)
- [HTTP adaptation](http-adaptation.md), [invocation filters](invocation-extensions.md) and [authorization](authorization.md)
- [Outbox and migration](transactional-outbox.md), [native EF](efcore-configuration.md) and [native hosting](native-hosting.md)
- [Source generator isolation](source-generators.md)
