# NOF.Hosting.Maui

[English](../../en/packages/maui.md)

MauiAppBuilder 的客户端 UI 注册。

调用 MauiApp.CreateBuilder().AddNOF()，配置原生 Maui 应用后 Build。生命周期由平台负责，NOF 不将其转换为第二套 IHost，也不启动服务端后台服务。平台构建使用仓库固定的 MAUI 工作负载。

## 安装

```sh
dotnet add package NOF.Hosting.Maui
```

## 用法与边界

- [native-hosting](../design/native-hosting.md)
- [net11-migration](../design/net11-migration.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
