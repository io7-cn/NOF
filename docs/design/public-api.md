# Public API Overview

The application programming model is proto-style IApplicationService declarations,
partial ApplicationService containers and generated Service.Operation overrides.
IRpcResponse and all client generation are removed. Policies reject execution separately
from business response unions.

Native HTTP APIs use ordinary feature mapping extensions, MapGet<Service.Operation>, declarative binding/response rules and runtime OpenAPI. See [HTTP adaptation](../en/design/http-adaptation.md).
Generated Service.Operation.Descriptor identifies an application operation for
HttpContext.InvokeAsync. Contract references no Hosting package or HTTP annotation.
Legacy HTTP RPC/JSON-RPC adapters, annotations, route generators and transport diagnostics
are deleted without a compatibility layer. HTTP routes are explicit native endpoint modules.

See the [service API and migration guide](../en/design/application-requests.md) and
[complete configuration REST implementation](../en/design/configuration-rest.md).

FailureCase declarations in Contract generate Application-owned closed outcomes. Name is the valid branch identifier; Title is a summary; custom Error is the optional third argument. Every case implements IFailure (Title/Detail), optionally IFailure<TError>. Match concrete types, never Title text. See [expected failures](../en/design/expected-failures.md).

Contract generates <Operation>Operation bases containing canonical nested Result/failure types. Application operation bases inherit them. Handlers use Task<Result> and new <Case>(...); all paths preserve Contract type identity. ServiceOperation abstractions now belong to NOF.Contract.

ExpectedOutcomeAttribute and NOF230/231/232 are removed; native C# union switch-expression exhaustiveness checks apply. No Create<Name> factories are generated.

Contract no longer exports generic Result/Result<T>, ResultJsonTypeClassifier, DictionaryExtensions or ContextExtensions. Generated operation-specific Result unions remain. OAuth application services use FailureCase; protocol interfaces own concrete native outcomes.
