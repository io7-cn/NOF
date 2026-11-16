# NOF.Hosting.BlazorWebAssembly

[简体中文](../../zh-CN/packages/blazor-webassembly.md)

Client UI registration for WebAssemblyHostBuilder.

Call WebAssemblyHostBuilder.CreateDefault(args).AddNOF(), then native Build/RunAsync. This registers UI services only, with no server workers or second host lifecycle. Remote HTTP integration is application-owned; server authorization remains authoritative.

## Installation

```sh
dotnet add package NOF.Hosting.BlazorWebAssembly
```

## Usage and boundaries

- [native-hosting](../design/native-hosting.md)
- [authorization](../design/authorization.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
