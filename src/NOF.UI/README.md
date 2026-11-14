# NOF.UI

Reusable UI package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

Provides shared UI-facing capabilities for browser clients:

- Authorization components (`AuthGuard`, `PermissionRouteView`)
- Browser storage abstractions (`ILocalStorage`, `ISessionStorage`)
- Browser info service, change events, and platform helpers (`IBrowserInfoService`, `BrowserInfoExtensions`, `NOF.UI.lib.module.js` JS initializer)
- JavaScript-backed browser info and storage services for Blazor clients

`NOF.Hosting.BlazorWebAssembly` depends on this package and focuses on host bootstrapping.

Authorization uses NOF.Abstraction directly, without a Contract/Application/Domain dependency. AddNOFUI installs the default checker; replace IPermissionGrantProvider through normal DI for dynamic grants. No directory registration is required. AuthGuard checks a permission ID or explicit UI Policy; it does not inspect UseCase contracts or page Attributes. PermissionRouteView accepts an explicit Policy. Identity changes cancel stale checks; server authorization remains authoritative. See [the authorization guide](../../docs/en/design/authorization.md) ([简体中文](../../docs/zh-CN/design/authorization.md)).

## Installation

```shell
dotnet add package NOF.UI
```

## License

Apache-2.0
