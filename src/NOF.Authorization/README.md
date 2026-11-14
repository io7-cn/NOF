# NOF.Authorization

Transport-independent permission checks and catalogs. Depends on NOF.Contract and NOF.Abstraction; has no dependency on Application, Domain or ASP.NET Core.

```csharp
services.AddPermissions(
    new PermissionDefinition("config.read", "Read configuration", "Configuration"),
    new PermissionDefinition("config.write", "Write configuration", "Configuration"));

var allowed = await permissions.IsGrantedAsync("config.read", cancellationToken);
```

Permission declarations belong exclusively to Contract interfaces and their operation methods. NOF execution resolves these declarations once into immutable policies; handlers carry no authorization declarations. Missing catalog references, conflicting declarations and ambiguous interface inheritance are configuration errors.

IPermissionChecker owns authentication and All/Any group semantics. Replace IPermissionGrantProvider to obtain grants from application-owned data while preserving those semantics. ClaimsPermissionGrantProvider reads permission claims from authenticated identities only. Definitions do not grant permissions.

UI controls check permission identifiers directly; they do not inspect or bind to UseCases. Server authorization remains authoritative. Native authentication establishes identity, while HTTP adapters own challenge/forbid presentation.

See [the authorization guide](../../docs/en/design/authorization.md) ([简体中文](../../docs/zh-CN/design/authorization.md)) for declaration rules, host setup and migration. Role/organization administration and persistence are outside this package.
