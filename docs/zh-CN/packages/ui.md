# NOF.UI

[English](../../en/packages/ui.md)

Blazor 权限组件与浏览器服务。

AddNOFUI 提供 AuthGuard、PermissionRouteView、浏览器存储与信息服务。鉴权直接依赖 Abstraction，UI 只检查明确权限标识或策略，不读取 UseCase 声明；身份变化会取消过时检查。动态授予通过依赖注入替换 IPermissionGrantProvider。页面可见性不是安全边界，操作仍需在服务端授权。

## 安装

```sh
dotnet add package NOF.UI
```

## 用法与边界

- [authorization](../design/authorization.md)
- [host-composition](../design/host-composition.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
