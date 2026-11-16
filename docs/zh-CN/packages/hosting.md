# NOF.Hosting

[English](../../en/packages/hosting.md)

ApplicationPart 与原生宿主装配。

使用原生 Builder 和 Build。AddApplicationPart 在容器内去重应用生成初始化器，AddApplicationService 注册服务映射。DeploymentOptions 与作用域延迟服务保留为装配工具，不提供第二套 IHost、初始化步骤管道或异步 Build 包装。启动工作由原生 Hosted 生命周期负责。

## 安装

```sh
dotnet add package NOF.Hosting
```

## 用法与边界

- [native-hosting](../design/native-hosting.md)
- [handler-registration](../design/handler-registration.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
