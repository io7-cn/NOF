# NOF.UI

[简体中文](../../zh-CN/packages/ui.md)

Blazor permission components and browser services.

AddNOFUI supplies AuthGuard, PermissionRouteView, browser storage and browser information services. Authorization depends directly on Abstraction; UI checks explicit permission identifiers or policies and never reads UseCase declarations. Identity changes cancel stale checks. Replace IPermissionGrantProvider through dependency injection for dynamic grants. UI visibility is not a security boundary; every operation still authorizes on the server.

## Installation

```sh
dotnet add package NOF.UI
```

## Usage and boundaries

- [authorization](../design/authorization.md)
- [host-composition](../design/host-composition.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
