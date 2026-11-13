# .NET 11 migration and union types

NOF 11.0.0 is the .NET 10 release baseline. The next major version, NOF 12,
targets .NET 11 and C# 15. NOF package versions are independent of .NET versions.

## Build requirements

`global.json` selects the stable .NET 11.0.100 SDK and workload set 11.0.100.1.
Runtime libraries, tests, samples, the website, and platform-specific MAUI targets
use `net11.0`. Roslyn generators and code fixes retain `netstandard2.0` so the
compiler can load them; their Roslyn dependencies move to 5.9.0 for C# 15 support.

ASP.NET Core, Blazor, JS interop, and Microsoft.Extensions packages use 11.x.
Extensions abstraction and Options packages provided by the runtime no longer
need explicit package references. OpenAPI.NET 3.11 uses `IOpenApiMediaType` in
request and response content dictionaries.

On Windows, install the pinned MAUI workload and Android dependencies:

```powershell
dotnet workload install maui
dotnet build src/Hostings/NOF.Hosting.Maui/NOF.Hosting.Maui.csproj `
    -t:InstallAndroidDependencies -f net11.0-android -p:AcceptAndroidSdkLicenses=True
dotnet restore NOF.slnx -p:Configuration=Release
dotnet format NOF.slnx --verify-no-changes --no-restore
dotnet build NOF.slnx -c Release --no-restore
dotnet test NOF.slnx -c Release --no-build
```

Use the pinned SDK for these commands. `global.json` also searches the ignored
`.dotnet/11` directory for a local SDK installation before the system location.

### Persistence dependency boundary

At migration time (November 11, 2026), Npgsql's EF Core 11 provider is available
only as a release candidate. EF Core and its PostgreSQL provider therefore remain
on a compatible stable 10.x combination. These libraries run on .NET 11. Upgrade
them together when the stable PostgreSQL provider is available; do not mix an
EF Core 11 runtime with a provider requiring EF Core 10.

## Adopted unions

The following outcome models have a fixed set of outcomes and now use
native C# 15 `union` declarations:

| Type | Cases | Boundary |
| --- | --- | --- |
| `Result`, `Result<T>` | `Empty` / `T`, shared `Failure` | RPC, JSON serialization, local and HTTP clients |
| `InboundAuthorizationResult` | `Allowed`, `Denied` | Authorization handler and inbound middleware |
| `OAuthClientCredentialsValidationResult` | `Success`, `Failure` | OAuth client repository and grant processing |
| `OAuthTokenExchangeResult` | `Success`, `Failure` | Token exchange handler and token endpoint |

Case records keep their nested names and payloads. Return a case directly from
an async handler, or specify the union as the generic argument of
`ValueTask.FromResult<OAuthTokenExchangeResult>(new OAuthTokenExchangeResult.Success(...))`.
The compiler applies the implicit case-to-union conversion.

Consumers can match the cases exhaustively:

```csharp
static string Describe(OAuthTokenExchangeResult result) => result switch
{
    OAuthTokenExchangeResult.Success success => success.Subject,
    OAuthTokenExchangeResult.Failure failure => failure.Error,
    null => "The handler returned an uninitialized result."
};
```

Generated unions are value types. Their default value contains null, so security
boundaries handle that case explicitly. An uninitialized authorization result
throws before any request, command, or notification handler executes. Client
authentication rejects it, and token exchange returns `server_error`.

This is a source and binary breaking change: custom types can no longer derive
from these result types, union instances cannot be cast to their case records,
and APIs receiving `object` see the union wrapper. Use typed patterns to extract
cases, or `.Value` when inspecting a case through object-based assertion APIs.
Recompile custom handlers against NOF 12. The OAuth HTTP response format remains
the existing protocol response; the union itself is not sent over the wire.

## Other candidates

| Existing model | Decision | Reason |
| --- | --- | --- |
| `Result`, `Result<T>` | Adopt native unions | T and shared Failure are separate cases; Payload-free Result uses Empty; successful messages belong to business DTOs. Success wrappers are removed. See the [breaking migration guide](../en/design/result-unions.md). |
| `Optional<T>` | Keep the current representation | Absent and explicitly present null are distinct, including for partial-update requests; `Value` and `HasValue` also define the JSON shape. |
| Streaming and paging results | Use typed success payloads | `Result<IAsyncEnumerable<T>>` and `Result<Page<T>>` replace the old result subclasses. |
| Cache and object-storage payloads | Keep generic contracts | Payloads form an open set; a closed union would restrict application types. |
| Payload-free enums | Keep enums | Cases carry no separate payloads. The former topology enum and initialization engine are removed. |

The Result migration uses tagged JSON, required case fields, source-generated
System.Text.Json metadata and explicit streaming-message transport. Its guide
describes coordinated deployment and rollback.

## References

- [C# union reference](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
- [System.Text.Json union serialization](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types)
- [ASP.NET Core 10 to 11 migration](https://learn.microsoft.com/aspnet/core/migration/100-to-110?view=aspnetcore-11.0)
- [Npgsql EF Core provider versions](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL)

Declared use cases can generate closed failure unions via [FailureCase](../en/design/expected-failures.md). The old Domain FailureAttribute/static-holder generator and failure-to-exception conversions are removed.

Generic Contract Result/Result<T>, ResultJsonTypeClassifier, DictionaryExtensions and ContextExtensions are removed in the final API. See [operation-specific unions](../en/design/result-unions.md).
