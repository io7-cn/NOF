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
