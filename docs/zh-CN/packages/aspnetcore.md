# NOF.Hosting.AspNetCore

[English](../../en/packages/aspnetcore.md)

原生 HTTP 操作绑定与 OpenAPI。

AddNOF 注册原生 JSON、ProblemDetails 与 OpenAPI 服务，宿主显式调用 UseNOF、MapOpenApi 和功能端点扩展。MapGet<Operation> 等方法保留静态操作标识；Request 绑定输入，Response<T>(status) 替换完整的精确类型响应映射。特殊响应和 SSE 使用原生 TypedResults。执行与 OpenAPI 共用不可变绑定，不执行展示回调以推断结构。void 需要明确的持久化接收端点。

## 安装

```sh
dotnet add package NOF.Hosting.AspNetCore
```

## 用法与边界

- [http-adaptation](../design/http-adaptation.md)
- [configuration-rest](../design/configuration-rest.md)
- [one-way-operations](../design/one-way-operations.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
