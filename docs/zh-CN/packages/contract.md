# NOF.Contract

[English](../../en/packages/contract.md)

传输无关的服务声明与数据模型。

服务方法使用同步声明、单个引用类型输入、业务成功类型，以及可选 FailureCase/权限注解。void 表示没有回信的单向操作，不能声明 FailureCase；Empty 明确保留响应。接口不需要 partial，Result、失败与描述符由 Application 生成。Contract 不包含客户端生成、HTTP 路由、通用 Result、执行上下文或执行器接口。Optional 区分缺失与存在且为 null。

## 安装

```sh
dotnet add package NOF.Contract
```

## 用法与边界

- [contract-boundaries](../design/contract-boundaries.md)
- [expected-failures](../design/expected-failures.md)
- [one-way-operations](../design/one-way-operations.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
