# Public API Overview

The application programming model is proto-style IApplicationService declarations,
partial ApplicationService containers and generated Service.Operation overrides.
IRpcResponse and all client generation are removed. Policies reject execution separately
from business response unions.

Native HTTP APIs live in IHttpEndpointModule, using ASP.NET Core binding and TypedResults.
Generated Service.Operation.Descriptor identifies an application operation for
HttpContext.InvokeAsync. Contract references no Hosting package or HTTP annotation.
Legacy HTTP RPC/JSON-RPC adapters, annotations, route generators and transport diagnostics
are deleted without a compatibility layer. HTTP routes are explicit native endpoint modules.

See the [service API and migration guide](../en/design/application-requests.md) and
[complete configuration REST implementation](../en/design/configuration-rest.md).

FailureCase declarations in Contract produce operation-specific closed outcomes. The first constructor argument is the required branch name. The optional second argument selects an opaque ErrorCode or typeof(CustomError). Default branches expose ErrorCode/Message; custom branches expose typed Error without an imposed code. The old Domain static Failure generator and exception conversions are removed. See [expected failures](../en/design/expected-failures.md).

Contract generates <Operation>Operation bases containing canonical nested Result/failure types. Application operation bases inherit them. Handlers use Task<Result> and new <Case>(...); all paths preserve Contract type identity. ServiceOperation abstractions now belong to NOF.Contract.

ExpectedOutcomeAttribute and NOF230/231/232 are removed; native C# union switch-expression exhaustiveness checks apply. No Create<Name> factories are generated.
