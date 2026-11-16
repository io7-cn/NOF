# Handler 注册

[English](../../en/design/handler-registration.md)

生成器提供程序集级 `AssemblyInitializeAttribute` 初始化器，`AddApplicationPart(assembly)` 将它们应用到当前依赖注入容器，不使用进程级类型目录或注册步骤管道。

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
```

Application 根据手写服务基类与 Contract 元数据注册作用域操作实现，不读取其他生成器的输出。操作以“服务 + 方法”标识，可以共享请求/响应模型。具体独立 `OperationHandler` 不需要 Contract 方法，同样由 ApplicationPart 发现。Outbox 与本地事件 Handler 也会自动发现，注册期间不会构造实例。

`ApplicationServiceRegistry`、`EventHandlerRegistry`、`MappingRegistry` 等目录属于单个容器，首次读取时冻结。应先添加所有所属程序集，再映射端点或执行操作。重复添加同一 ApplicationPart 会在容器内去重；测试程序集包含映射或 Handler 时也要显式添加。框架不会隐式发现调用者程序集，不生成客户端，也不会因发现 Handler 自动开启 Outbox Worker。

参见[应用操作](application-requests.md)、[独立操作](unified-operations.md)和[生成器隔离](source-generators.md)。
