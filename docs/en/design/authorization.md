# Contract authorization and permission catalogs

[简体中文](../../zh-CN/design/authorization.md) · [Host composition](host-composition.md)

NOF owns permission declarations, checking semantics and execution enforcement. Native authentication validates credentials and creates the trusted ClaimsPrincipal. NOF.Abstraction provides shared permission models, immutable policies, catalogs and default checking without referencing Contract, Application, Domain or ASP.NET Core. Contract references Abstraction only for these shared models and retains declaration attributes; Application owns AuthorizationPolicyResolver. UI references Abstraction directly and has no Contract or UseCase dependency. There is no standalone authorization package.

## Declare permissions only on Contract interfaces

```csharp
using NOF.Abstraction;
using NOF.Contract;

[RequireAuthenticated]
public interface IConfigService : IApplicationService
{
    [RequirePermission("config.read")]
    ConfigDto Read(ReadConfigRequest request);

    [RequirePermission("config.write", "config.admin", Match = PermissionMatch.Any)]
    Empty Update(UpdateConfigRequest request);

    [AllowAnonymous]
    PublicConfigDto ReadPublic(ReadPublicConfigRequest request);
}
```

RequirePermission requires at least one nonblank identifier and implies authentication. Its default Match is All. Multiple groups on one declaration combine with AND. For example, separate `RequirePermission("config.read")` and `RequirePermission("owner", "admin", Match = PermissionMatch.Any)` mean read AND (owner OR admin).

An operation declaration replaces interface defaults. Nearer interface/method declarations replace inherited defaults. AllowAnonymous cancels inherited defaults within that Contract; it cannot bypass host authentication or tenant constraints. An unannotated Contract preserves anonymous access. Handlers, request/message types and UI pages are not sources of permission declarations.

NOF240 reports empty/invalid groups; NOF241 reports anonymous and authenticated/permission declarations on the same member; NOF242 rejects implementation-method declarations. Runtime resolution also validates custom Attributes implementing IPermissionMetadata. Sibling inherited declarations must agree on permission groups, independent of enumeration order; ambiguity fails configuration and requires an explicit derived declaration. There is no common MetadataAttribute, string key protocol or IMetadata marker.

## Define available permissions separately from grants

```csharp
services.AddPermissions(
    new PermissionDefinition("config.read", "Read configuration", "Configuration"),
    new PermissionDefinition("config.write", "Write configuration", "Configuration"),
    new PermissionDefinition("config.admin", "Manage configuration", "Configuration"));
```

AddNOF and AddNOFUI install default checking services, so hosts only add their definitions. PermissionCatalog lists immutable definitions in stable order. IDs use ordinal case-insensitive comparison; duplicate IDs fail, and configuration freezes on first use. Separate service collections have independent catalogs. Defining a permission never grants it.

Every referenced permission, including every Any alternative, must exist. Default execution validates generated operation registrations before serving and validates manually supplied descriptors before execution. Unknown identifiers are configuration errors, including direct IsGrantedAsync calls. Register definitions before describing endpoints, resolving a catalog or starting the host. Explicitly add the same definitions to a separate UI host; no permission/role database or migration is installed.

## Check permissions and replace the grant source

```csharp
var granted = await permissions.IsGrantedAsync("config.read", ct);
```

IPermissionChecker uses the scoped IUserContext and current semantic Context for this convenience API. CheckAsync accepts an immutable AuthorizationPolicy, explicit ClaimsPrincipal and Context and returns Granted, AuthenticationRequired or Denied. Groups are snapshotted; mutable Attributes or caller arrays cannot change an effective policy.

The default ClaimsPermissionGrantProvider accepts exact and wildcard permission claims from authenticated identities only. OAuth scopes and roles are not automatically interpreted as business permissions. An unrelated unauthenticated identity cannot add a grant to an authenticated principal.

For application-owned grant data, register a scoped IPermissionGrantProvider using normal DI. It receives the identity, one defined permission, explicit tenant context and cancellation token. NOF still owns authentication, Any/All and denial handling. Provider exceptions propagate as technical failures; they never become grants. This version provides no role or organization administration.

Execution, failure description and OpenAPI use the same cached Contract policy. HTTP, local invocation and message consumption check it before constructing business handlers. Tenant trust is checked before constructing or querying grant providers. The provider is resolved lazily inside the explicit authenticated tenant context, so scoped persistence captures the trusted tenant. Anonymous denials never construct the grant provider. Messages do not restore producer claims/tokens; hosts explicitly establish any trusted consumer identity. A custom invocation engine retains responsibility for enforcing its declared plan.

Generated operation initializers preserve inherited Contract methods for trimming. Standalone trimmed callers of AuthorizationPolicyResolver must also preserve their selected Contract's inherited method declarations.

## UI checks presentation permissions directly

```razor
<AuthGuard Permission="config.write">
    <ChildContent><button>Edit</button></ChildContent>
    <UnauthorizedContent><span>Read-only</span></UnauthorizedContent>
</AuthGuard>
```

AuthGuard calls the shared checker; Policy accepts explicit UI-owned Any/All groups when needed. Do not specify both Policy and Permission. Without either, AuthGuard requires authentication. A route wrapper receives an explicit Policy; it does not read page Attributes or infer UseCases. Native RouteView remains sufficient for unrestricted routes.

UI and server use the same catalog/checking semantics, but may have different identity state. UI visibility is not a security boundary and never substitutes for server checking. Identity changes trigger rechecking; stale asynchronous grants cannot overwrite newer denials. No UI-to-UseCase mapping or role-expression DSL is introduced.

## Sample and migration

SamplePermissions defines the shared catalog. IPermissionDemoService.GetOverview declares its Any group in Contract; PermissionDemoService has an ordinary handler. The HTTP adapter exposes `/api/permissions/overview`; native JWT tests cover 401, 403 and both allowed alternatives. `/permissions-demo` displays the catalog and uses AuthGuard directly by permission ID. Existing CRUD access rules remain unchanged. The sample does not mint business grants from OAuth scopes or invent a UI login flow.

Replace bare/empty RequirePermission with RequireAuthenticated. Move former handler permissions to the appropriate Contract method and review the intended access policy. Remove MetadataAttribute and its string keys; custom metadata uses ordinary Attributes and purpose-specific consumers. SummaryAttribute now directly inherits Attribute; BCL Description/Category remain usable. Replace page permission Attributes with explicit UI checks and pass Policy to PermissionRouteView, or use native RouteView. No database schema change is required.

Import NOF.Abstraction for permission models, policies, catalogs and checks, and NOF.Application for AuthorizationPolicyResolver. Replace any standalone authorization package reference with the owning existing package. The old ClaimsPrincipal.HasPermission path is removed; use IPermissionChecker so catalog validation and authentication semantics stay consistent.
