# Framework boundaries and native platform APIs

[简体中文](../../zh-CN/design/framework-simplification.md) · [Host composition](host-composition.md) · [Authorization](authorization.md) · [Persistence](persistence-boundaries.md)

NOF describes and executes business operations. Shared business semantics belong in the existing layers; native host and provider APIs supply transport, lifecycle and technical errors. Avoid adding a package or a forwarding service when an existing owner is sufficient.

## Permission ownership

| Owner | Responsibility |
|---|---|
| Abstraction | PermissionMatch, IPermissionMetadata, immutable policies, IPermissionChecker and the default claim-grant implementation |
| Contract | RequirePermission, RequireAuthenticated and AllowAnonymous declarations on interfaces and operation methods |
| Application | AuthorizationPolicyResolver interprets Contract defaults and inheritance |
| Infrastructure | Cached invocation policy and enforcement before handler construction, after tenant trust checks |
| UI | Checks identifiers or explicit UI policies using Abstraction; no Contract or UseCase inspection |

Contract references Abstraction to share the permission models. Abstraction references no other NOF runtime package, and Domain remains independent of Contract. UI and Hosting also reference only Abstraction among NOF runtime packages. No separate authorization package or compatibility facade remains.

## Persistence failures

Application retains IUnitOfWork and the minimal transaction interfaces without defining database exceptions. Saving has only the standard distinctions needed by callers:

| Event | Reported failure |
|---|---|
| EF Core optimistic-lock failure or NHibernate stale state | System.Data.DBConcurrencyException, with the native cause as InnerException |
| EF Core update failure or NHibernate ADO failure | System.Data.DataException, with the native cause as InnerException |
| Transaction begin/commit/rollback failure | Original native exception |
| Cancellation or an unexpected application/provider-hook error | Original exception |

Remove NOF.Application.DbException, DbUpdateException, DbUpdateConcurrencyException, DbTransactionException and DbTransactionCommitException catches/imports. Use the BCL save errors above when a provider-independent distinction is needed; native transaction diagnostics and cancellation are preserved.

Message endpoints execute operations directly in an isolated scope with restored Context and the normal policy/filter pipeline. NOF owns no Inbox storage, deduplication or consumer retry scheduler. Business code owns idempotency and transaction/save boundaries; RabbitMQ ACK follows operation completion. Native adapters own retries and dead-letter policy. Memory calls await execution and propagate failures. AddOutbox alone enables durable outgoing intents and workers; queue mapping enables no persistence. See [direct endpoints and migration](transactional-outbox.md).

## Removed duplicate entry points

| Removed API | Replacement |
|---|---|
| ReplaceOrAdd and ReplaceOrAddSingleton/Scoped/Transient | Native services.Replace(ServiceDescriptor.Singleton/Scoped/Transient(...)), from Microsoft.Extensions.DependencyInjection.Extensions |
| DelegateBackgroundService and delegate AddHostedService overloads | Native AddHostedService<T> with an ordinary BackgroundService or IHostedService |
| HttpRequestInboundAdapter | Existing HttpContext.InvokeAsync and fluent HTTP endpoints invoke IOperationExecutor directly |
| ClaimsPrincipal.HasPermission | IPermissionChecker; catalog validation and authenticated-grant semantics have one implementation |
| ClaimsPrincipal.GetIdentity/GetIdentities | Native Identities.OfType<T>() queries |
| JwtClaimsIdentity | Native host authentication and ClaimsIdentity; no token parser that implicitly marks unverified claims authenticated |
| Empty NOFApplicationConstants and unused service-method reflection helper | Existing concrete constants and OperationDescriptor.MethodInfo |

The event registration generator also emits native Replace. Provider selectors keep their existing last-registration behavior. HTTP identity is copied before semantic context selectors run, and both explicit and fluent endpoints retain the common authorization boundary. Hosting no longer requires the JWT library; hosts select their identity protocol dependencies.

Update runtime packages, providers and generators together. Permission directories and their display DTOs belong to the application; AddPermissions, PermissionCatalog and PermissionDefinition are removed. Cached plans never cache grant decisions; each check calls its provider for current data. Outbox persists every JSON-scalar Context item and prepares complete immutable delivery Headers; fresh attempt scopes bind the restored Context before handler construction. Native clients forward OutboxDeliveryContext.Headers directly; MessageDelivery and its registrations are removed.

Import NOF.Abstraction for shared permission types and NOF.Application for AuthorizationPolicyResolver. These API changes require no database migration and do not reinterpret persisted messages.

See [JSON context and automatic durable delivery](execution-context.md) for the breaking Context API, ready-to-send headers and fluent registration.
