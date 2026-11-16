# NOF.Hosting.Maui

[简体中文](../../zh-CN/packages/maui.md)

Client UI registration for MauiAppBuilder.

Call MauiApp.CreateBuilder().AddNOF(), configure the native Maui app and Build. The platform owns lifetime; NOF does not adapt it to a second IHost or start server background services. Install the pinned MAUI workloads for platform builds.

## Installation

```sh
dotnet add package NOF.Hosting.Maui
```

## Usage and boundaries

- [native-hosting](../design/native-hosting.md)
- [net11-migration](../design/net11-migration.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
