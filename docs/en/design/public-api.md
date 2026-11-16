# Public programming model

[简体中文](../../zh-CN/design/public-api.md)

Contract contains declarations and data models only. Application owns generated operation bases, descriptors and closed outcomes. Infrastructure selects execution filters and persistence capabilities; native hosts own authentication, transports and lifecycle. Domain contains business invariants and depends on no transport or Contract layer.

| Capability | Public entry |
| --- | --- |
| Service operations | `IApplicationService`, `ApplicationService<TService>`, generated `Service.Operation` |
| Internal operations | `OperationHandler<TRequest>` or `OperationHandler<TRequest,TResponse>` |
| Expected failures | Contract `FailureCase`; Application-generated `Result` and distinct branches implementing `IFailure` |
| Complete local invocation | `Service.Operation.InvokeAsync` or `IOperationExecutor.ExecuteAsync` |
| HTTP | Native endpoint modules and `MapGet<Operation>` / related mappings |
| Durable outgoing work | `IOutbox`, one concrete `OutboxHandler<T>` per payload type |
| Persistence | Application-owned ports, `IUnitOfWork` and optional transaction interfaces |
| Shared context | Immutable `NOF.Abstraction.Context` with JSON scalar items |

Service interfaces are not partial. Methods declare their successful business type; void means no reply, while Empty retains a reply and expected failures. Host policy rejection is distinct from a business failure. Match concrete cases, never display text. Successful messages belong to explicit data models.

There is no generic Contract Result, generated client, HTTP RPC compatibility adapter, Inbox, Domain exception hierarchy or OIDC server package. Use native C# union matching and host HTTP resource/ProblemDetails mapping. Coordinate breaking upgrades across all owning packages and consumers; durable payload renames require data migration.

Read [Contract boundaries](contract-boundaries.md), [expected failures](expected-failures.md), [HTTP adaptation](http-adaptation.md) and the [package index](../packages/index.md).
