# NOF.Hosting.AspNetCore

[简体中文](../../zh-CN/packages/aspnetcore.md)

Native HTTP operation binding and OpenAPI.

AddNOF registers native JSON, ProblemDetails and OpenAPI services. Call UseNOF, MapOpenApi and feature mapping extensions explicitly. MapGet<Operation> and related methods preserve static operation identity; Request rules bind inputs and Response<T>(status) replaces a complete exact-type response map. Native TypedResults handles special responses and SSE. Runtime execution and OpenAPI use the same immutable binding without executing user presentation callbacks. Void requires an explicit durable-acceptance endpoint.

## Installation

```sh
dotnet add package NOF.Hosting.AspNetCore
```

## Usage and boundaries

- [http-adaptation](../design/http-adaptation.md)
- [configuration-rest](../design/configuration-rest.md)
- [one-way-operations](../design/one-way-operations.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
