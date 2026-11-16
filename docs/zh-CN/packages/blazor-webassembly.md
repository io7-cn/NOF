# NOF.Hosting.BlazorWebAssembly

[English](../../en/packages/blazor-webassembly.md)

WebAssemblyHostBuilder 的客户端 UI 注册。

调用 WebAssemblyHostBuilder.CreateDefault(args).AddNOF()，再使用原生 Build/RunAsync。这里只注册 UI 服务，不开启服务端 Worker 或第二套宿主生命周期。远程 HTTP 集成由应用维护，服务端授权始终是最终边界。

## 安装

```sh
dotnet add package NOF.Hosting.BlazorWebAssembly
```

## 用法与边界

- [native-hosting](../design/native-hosting.md)
- [authorization](../design/authorization.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
