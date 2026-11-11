# ADR 0001: Application execution core and transport adapters

[简体中文](../../zh-CN/design/0001-application-core-and-adapters.md) · [Development guide](../README.md)

- Status: Accepted direction; phased implementation. Each implementation requires normal independent code review.
- Date: 2026-11-11
- Owner and acceptance owner: XmmShp
- Reviewers: qualified domain and framework reviewers are required for each merge candidate.
- Tracking: [issue #4](https://github.com/io7-cn/NOF/issues/4)
- Prerequisite: [.NET 11 / Result migration, PR #3](https://github.com/io7-cn/NOF/pull/3)
- Supersedes the RPC-only application execution decision in [request dispatch](../../design/request-dispatch.md) as phases are delivered.

## Context and constraints

NOF aims to support DDD and Clean Architecture across HTTP, local calls, workers
and message consumers. ASP.NET Core supplies HTTP behavior; NOF supplies application
execution conventions, domain boundaries and reliable persistence/messaging.
DDD does not require a microservice deployment or a framework base class for every entity.
Simple CRUD use cases remain valid without a rich aggregate model.

The existing RpcHandler requires IRpcResponse and its inbound context carries an
IUnion response. Failure has been unified in Abstraction, but its JSON discriminator
and the Result transport factories still couple business values to serialization/RPC.
Domain IRepository exposes LINQ, tracking and attachment operations. Merely moving
these types into an Abstraction assembly does not make their contracts neutral.

The transition must preserve tenant isolation, authorization, cancellation,
transaction/outbox consistency, current RPC clients, generated invocation and Native
AOT. Existing releases and tags are immutable. Work depending on unmerged changes
must identify that prerequisite and must not bypass protected-main review gates.

## Options

1. Expand NOF into a parallel web framework with binding, HTTP result types, response
   mapping attributes and its own OpenAPI model. This provides uniform syntax but
   duplicates ASP.NET Core and makes HTTP semantics increasingly central.
2. Keep RPC as the only application entry point. This preserves current conventions
   but forces local use cases and REST endpoints into RPC response constraints.
3. Introduce a transport-independent application execution core with thin adapters.
   This requires an incremental split of policies and metadata, but lets each host
   retain its native facilities and lets business handlers run without a web server.

## Decision

Choose option 3. A use case accepts a typed request and execution context and returns
its declared business response. Responses may be DTOs, nullable values, Result<T>,
or business-specific unions; no RPC response interface is required by the core.
### Developer-facing service model

Retain service contracts, generated operation bases and the existing
`XxxService.DoSomething` override workflow. A use case is the application operation
represented by that model, not a second public handler hierarchy beside RPC.
Evolve RPC-centered service/server/handler contracts into transport-independent
application service equivalents. Ordinary DTOs and business unions must not require
IRpcResponse or a mandatory transport declaration. RPC becomes an adapter.

Operation identity is the service contract plus operation, not the request/response
pair. Different operations may share those models while retaining distinct behavior
and policies. Source-generated application-part registration remains the default.
The execution/policy machinery can remain behind generated invocation; applications
should not need a second use-case class, thin RPC forwarding class, or a separate
public executor API to express one service operation.

The current IRequestHandler/IRequestExecutor implementation in draft PR #5 is an
execution prototype validating response freedom, rejection, context and AOT. Its
parallel public programming model is provisional and must be consolidated into the
service model before this architecture is considered implemented.

The application policy pipeline handles application authorization, validation,
tenant boundaries, transaction policies and idempotency. Authentication mechanisms,
HTTP binding/status/headers, message acknowledgment/redelivery and wire serialization
belong to adapters. Domain invariants remain in the domain model. Validation and
application authorization must also run for local and non-HTTP callers.

Execution rejection is a separate channel from the handler's response. A business
Failure returned by a handler is not an authentication or policy rejection. A valid
null business response is distinct from an uninitialized execution outcome. Handler
resolution happens after policies so rejected calls do not construct or invoke the
business handler and context normalization can precede scoped dependency resolution.

### Responsibility profile

| Capability | Direction | NOF responsibility |
| --- | --- | --- |
| HTTP routing, binding, content negotiation, files, ranges and conditionals | Reuse ASP.NET Core | Thin entry points into use cases |
| Status codes, Location, ETag and ProblemDetails | Keep in HTTP adapters | Native TypedResults and optional mappings |
| OpenAPI | Reuse the host's metadata/schema pipeline | Supply accurate endpoint metadata |
| DI, configuration, logging and Host lifecycle | Reuse .NET | Conventions and optional convenience builders |
| OAuth/OIDC and token verification | Prefer established protocol implementations | Business identity, permission and tenant integration; evaluate replacement separately |
| Typed application execution and policies | Retain and strengthen | Consistent invocation across entry points |
| Domain rules, value objects and aggregate boundaries | Retain and strengthen | Optional primitives, analyzers and isolated tests |
| Transactions, domain/integration events, outbox/inbox and idempotency | Retain and strengthen | Defined atomicity and recovery behavior; avoid two competing delivery engines |
| Generic ORM facade | Narrow | Aggregate-oriented write ports and application query ports; provider implementations outside the domain |
| UI, storage, cache, mapping and state machines | Optional modules | Independent usefulness rather than mandatory core dependencies |

Keep shared Failure as a business value. Move wire discriminators, JSON configuration
and RPC empty-response/failure construction out of that value/core Result in a later
compatible migration step. Preserve explicit successful Message metadata as an
optional Success<T> payload, not a requirement for ordinary successful responses.

Execution context should expose verified semantic identity/tenant metadata. Adapters
translate raw headers and claims into that context. Reuse Activity/OpenTelemetry
rather than creating a competing tracing runtime. Ambient access remains optional;
explicit context is the testable contract.

Persistence changes must narrow provider-shaped APIs such as Attach, AsNoTracking
and SaveChanges(bool). Aggregate writes preserve aggregate boundaries. Read models
can use specialized query services. EF Core owns its transaction mechanism; NOF owns
the consistency contract between business state and staged integration messages.
Do not replace a working outbox solely to adopt another library; compare semantics,
recovery behavior, licensing and migration costs first.

## Delivery phases and consequences

1. **Application execution:** introduce ordinary typed handlers, typed policy execution
   and explicit execution rejection. Provide a native ASP.NET Core vertical slice and
   a supported RPC bridge. Keep legacy RPC authorization/tenant checks in force.
2. **Policy and context split:** migrate shared application rules to the application
   pipeline and protocol rules to adapters. Preserve security behavior at every stage;
   do not silently remove authentication/authorization while avoiding duplicate checks.
3. **Pure outcomes and codecs:** remove IRpcResponse and JSON metadata from core
   business outcomes, retaining wire classification in codecs and generated metadata.
   Coordinate clients and server contracts when the wire shape changes.
4. **Persistence boundaries:** introduce aggregate-oriented write ports, separate read
   models and narrow the universal ORM facade with provider parity tests.
5. **Optional integrations:** simplify host wrappers and review protocol, UI, storage
   and state-machine modules according to actual consumer needs and maintenance cost.

HTTP response DSL growth is not the main delivery path. An HTTP endpoint may use
native TypedResults to translate the completed business value or execution rejection.
File/conditional/stream response details remain in the HTTP adapter.

## Verification, rollout and rollback

Each phase must have an executable vertical slice and meaningful regression tests.
Verify DTO, Result/union and nullable returns, cancellation, scoped handler lifetimes,
context propagation, policy ordering, rejection before handler construction, tenant
isolation and authorization. Verify generated HTTP/local clients and native AOT
when their paths change. Keep domain tests independent of HTTP/database/broker hosts.

Roll out entry points incrementally. Keep supported RPC adapters working while new
application handlers are adopted. Record exact candidate evidence in the issue/PR,
not one-off reports in source control. Rollback restores the coherent previous
package and host configuration set; do not mix incompatible contracts or wire codecs.

Revisit this decision if adapter duplication becomes substantial, required security
semantics diverge across entry points, or measured AOT/performance constraints cannot
be met with the chosen integration. New abstractions require an actual consumer and
tests, rather than speculative generality.

## References

- [Phase 1 implementation guide](application-requests.md)

- [Clean Architecture and application boundaries](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [DDD-oriented services](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice)
- [ASP.NET Core responses](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses)
- [EF Core transactions](https://learn.microsoft.com/ef/core/saving/transactions)
