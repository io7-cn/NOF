# Code and documentation organization

[简体中文](../../zh-CN/development/code-organization.md)

Apply the pinned [shared style](../../../.standards/docs/en/02-development/coding-style.md). This page defines NOF's concrete layout; it does not change public namespaces or durable payload identities.

## Source

Group core projects by responsibility: Abstractions for ports, Models for reusable data, Services for implementations, Extensions for composition, and capability folders such as Authorization/Invocation/Outbox for cohesive subsystems. Provider and hosting projects keep their native adaptation concerns together. Domain owns invariants; Contract declarations do not depend on Application, Domain or Infrastructure.

One top-level type per file is the default. Keep a generic/non-generic family of the same name together. Keep an operation request and its direct response together in `<Operation>Request.cs`; reusable models/enums have their own Models files. Service interfaces live at the Contract root. Keep test-only fixtures with their owning behavior tests when that makes them easier to understand. These are purposeful groupings, not a reason to collect unrelated public types in an aggregate file.

Split partial classes use a purpose suffix. Keep compiler-generated members and checked-in native migrations separate from handwritten source; regenerate migrations with EF tooling matching the pinned EF runtime, then review their model/schema/data effects. Do not mechanically reorganize migration designers or generated code. Source generator snippets are data and must not be rewritten by plain-text renaming.

Static fields/constants use PascalCase, private instance fields use _camelCase, and local functions/variables/parameters use camelCase. Use C# Async suffixes where the interface permits them. Keep existing external/serialized names stable. Methods own one coherent operation, handle expected failures at the boundary and propagate technical errors/cancellation.

## Frontend

The root .editorconfig and .prettierrc.json match the shared baseline. C#/Razor use four spaces; JavaScript/CSS/JSON/YAML/project XML use two. package.json/package-lock.json pin the formatter. Run npm ci --ignore-scripts and npm run check:frontend. Third-party wwwroot/lib assets and generated outputs are excluded. Global styles use feature prefixes; Blazor component styles remain next to their component.

## Documentation

Full maintained prose lives under matching docs/en and docs/zh-CN paths with reciprocal links. Package/root README, CONTRIBUTING and SECURITY files remain short tool-discoverable portals. Obsolete docs/design files are removed after their useful content is migrated; update callers and navigation instead of retaining redirect shells. The generated API reference follows the public XML comments; bilingual guides explain its boundaries without translating code identifiers.

Update both editions and affected navigation in one change. The documentation check verifies edition parity, reciprocal links and local maintained links; DocFX validates the generated reference/site. Operational guides name an owner and review date. Task evidence belongs in ignored artifacts or external issue/PR/CI records.
