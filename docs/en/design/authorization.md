# Contract authorization and dynamic permissions

[简体中文](../../zh-CN/design/authorization.md) · [Host composition](host-composition.md)

NOF owns permission declarations, authentication requirements and All/Any checking semantics. Native authentication establishes the trusted ClaimsPrincipal. Abstraction owns immutable policies and the default checker/grant provider; Contract owns declaration attributes and Application owns AuthorizationPolicyResolver. UI uses Abstraction directly without reading Contract or UseCases. Permission directories and their storage belong to the application.

## Declare operation requirements

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

RequirePermission requires nonblank identifiers and implies authentication. All is the default; separate groups combine with AND. Method declarations replace interface defaults. Only actual descendant declarations override ancestors; competing inherited branches must agree or explicitly resolve ambiguity. AllowAnonymous cancels this Contract's inherited defaults, without bypassing native host or tenant requirements. Unannotated contracts permit anonymous access.

Declarations belong exclusively to Contract interfaces/methods. Handlers, request/message types and UI pages are not declaration sources. NOF240 checks invalid groups, NOF241 rejects contradictory anonymous/authenticated declarations and NOF242 rejects implementation targets. Custom Attributes may implement IPermissionMetadata; there is no generic string-key metadata protocol.

## Dynamic permission data

AddNOF and AddNOFUI install the default scoped checker automatically, through normal Abstraction registration. There is no AddPermissions API, framework PermissionDefinition or frozen PermissionCatalog. Identifiers do not need registering before startup or endpoint description. The framework validates declaration shape, not membership in an application dictionary.

```csharp
services.AddScoped<IPermissionGrantProvider, DatabasePermissionGrantProvider>();

var granted = await permissions.IsGrantedAsync("config.read", ct);
```

The application-owned provider queries current grants for the supplied ClaimsPrincipal, permission identifier and explicit semantic Context. It may use a database, external service or application-managed cache. Missing, disabled or ungranted permissions should return false. Names, groups, directory administration, role data and cache invalidation are application concerns; NOF introduces no directory-provider facade or storage schema.

Contract policy snapshots may be cached. Authorization decisions are evaluated on each invocation and are not cached by NOF: adding or revoking grants can affect the next check in the same scope without restarting, rebuilding DI or invalidating a plan. A provider owns any freshness limits of its own cache. Register custom providers before or after default composition; repeated default registration preserves overrides.

The default ClaimsPermissionGrantProvider reads exact/wildcard permission claims from authenticated identities only. It does not consult a directory, translate OAuth scopes/roles or query dynamic storage. Claim changes depend on the host's identity refresh; choose a database-backed provider when server-side revocation must be checked immediately.

## Execution and context

IPermissionChecker.IsGrantedAsync uses the scoped IUserContext and Context.Current. CheckAsync accepts an explicit immutable AuthorizationPolicy, principal and Context, returning Granted, AuthenticationRequired or Denied. Separate groups combine with AND, while each group applies its own All/Any mode. Provider exceptions and cancellation propagate; they never grant access.

HTTP, local calls and message consumption share the cached Contract requirements, checked before business handler construction. Tenant trust is validated before constructing/querying the grant provider. The provider resolves lazily under the explicit authenticated tenant context, preserving tenant-scoped persistence and restoring the ambient caller. Anonymous denials do not construct providers. Messages do not restore producer claims or tokens; the host establishes a trusted consumer identity. Custom execution engines must enforce their effective plans.

Generated operation initializers preserve inherited Contract methods for trimming; standalone trimmed resolver callers must preserve the selected inherited declarations too.

## UI and Sample

```razor
<AuthGuard Permission="config.write">
    <ChildContent><button>Edit</button></ChildContent>
    <UnauthorizedContent><span>Read-only</span></UnauthorizedContent>
</AuthGuard>
```

AuthGuard checks a permission identifier or an explicit UI-owned Policy; do not pass both. With neither, it requires authentication. PermissionRouteView requires an explicit Policy and never inspects page Attributes or UseCase contracts. Identity changes trigger rechecking and stale asynchronous grants cannot overwrite newer denials. Changes in external grant data need application-owned UI refresh/invalidation; there is no automatic database subscription. UI visibility never replaces server enforcement.

SamplePermissions contains example-owned SamplePermission display data only. IPermissionDemoService declares an Any group; its ordinary handler returns that example data, and `/permissions-demo` uses AuthGuard by identifier. The API's JWT tests cover 401/403 and both allowed alternatives, without any permission registration. The sample list is not an enforcement whitelist, and no role/organization administration is added.

## Migration

Delete AddPermissions calls and framework directory/model dependencies. Move directory DTOs and queries into application-owned contracts/services if needed, and implement IPermissionGrantProvider for dynamic grants. Import NOF.Abstraction for checking models and NOF.Application for Contract resolution. Keep permission annotations on Contract only; use RequireAuthenticated for login-only access. Replace old page Attributes with explicit UI checks and removed HasPermission calls with IPermissionChecker. No database schema migration is required by this change.
