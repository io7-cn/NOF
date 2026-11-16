# HTTP contract documentation

[简体中文](../../zh-CN/design/http-contract-documentation.md)

OpenAPI describes the published HTTP contract, including known input and business errors. It cannot promise all possible infrastructure failures, cancellation timing, disconnections or post-header stream failures. Do not infer exhaustive wire behavior from a business union alone.

## Ownership

| Concern | Owner |
| --- | --- |
| Projected request presence and nullability, actual binding sources/defaults | NOF HTTP adapter |
| Built-in JSON/parser 400 and JSON media-type 415 | The binder that produces them |
| Effective business/filter branches and the final selected response mapping | NOF HTTP adapter |
| Contract Summary/Description and default unique operation identity | NOF HTTP adapter; native endpoint metadata can override |
| Native authentication schemes, cookies, antiforgery and redirects | Application host |
| Public business error payload, stable problem type and Location | Application endpoint module |
| Domain input constraints, action values and application SSE event semantics | Application endpoint module |
| Ordinary Minimal API response types and opaque delegates | Native TypedResults/Produces metadata and application transformers |

Native WithName keeps its operation ID; otherwise the adapter derives a unique ID from operation identity, HTTP method and route, including when one operation has multiple bindings.

The adapter keeps required presence separate from allowed null. Its immutable input description preserves JsonPropertyInfo.IsSetNullable when splitting request members; the schema wraps references instead of modifying shared component definitions. Constructor-based body contracts keep their native schema. Built-in body readers advertise 400/415; an opaque custom reader must declare its own actual metadata, and a custom text body does not acquire an invented JSON 415.

Each effective failure contributes its branch name or filter catalog identity and declared title to response descriptions. Multiple branches sharing status/media/body keep one schema and all known branch descriptions. This is documentation, not a text-based runtime classifier or an automatic machine error code. Default failures remain ProblemDetails from Title/Detail; NOF does not expose arbitrary custom Error values automatically. Filter exclusions remove their cases from both execution and documentation. Response factories are never run to inspect behavior.

## Sample native HTTP decisions

Sample has named response DTOs and explicit Produces metadata for its development workbench. Invalid actions and missing order IDs use 400 ProblemDetails; value-object rejection has its own typed 400 body, while antiforgery errors use application/problem+json. Native JSON input errors are rendered through the host's ProblemDetails status-code pages. Receipt reads are a defined gateway: valid downstream data becomes MessageReceiptsResponse; downstream failure, invalid JSON or connection failure becomes 502 ProblemDetails. Request cancellation still propagates.

Deleting a node with children explicitly returns NodeHasChildrenProblem with an error property containing NodeHasChildrenError. Its type is urn:nof:sample:config-node:has-children. The complete native response mapping and matching Produces metadata belong to Sample; the generic framework failure format is unchanged. The created-node response documents its native Location header.

The host documents cookie-session OR JWT bearer authentication for permission-protected operations, plus the actual required permission groups. Login/logout are browser redirect flows through the external provider. Workbench POSTs and logout require both X-CSRF-TOKEN and nof.sample.antiforgery: first GET /api/demo/security-token, retain the response cookie, and send its token. Session credentials and antiforgery credentials are different concerns.

SSE uses OpenAPI 3.2 itemSchema for an event object whose data string contains JSON SampleStreamEvent; it is not one JSON document for the whole connection. The application documents its tenant header, started/tick/completed messages, default topic, count clamping to 1–20 and interval clamping to 100–5000 ms. These ranges describe clamping, not validation rejection. Standard event framing remains ASP.NET Core's responsibility.

## Verification

Contract tests generate the document and make real TestServer requests: nullable values, malformed JSON, wrong media types, structured errors, Location, antiforgery rejection/acceptance, native redirects and downstream 502. Schema tests also cover failure descriptions, excluded filters, SSE item framing and security requirements. Add a test when a known wire branch changes; do not claim untested technical failures are exhausted.

See [HTTP adaptation](http-adaptation.md), [configuration CRUD](configuration-rest.md) and [Sample workbench](sample-workbench.md). Native metadata follows [ASP.NET Core OpenAPI](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/include-metadata); stream representation follows [OpenAPI 3.2 SSE](https://spec.openapis.org/oas/v3.2.0.html#special-considerations-for-server-sent-events).

Business-specific native operation transformers are attached to the owning endpoint, rather than selected by route-string matching in the global transformer.
