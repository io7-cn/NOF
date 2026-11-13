# 显式 Ambient 绑定

[English](../../en/design/ambient-binding.md) · [原生宿主](native-hosting.md)

保留 IdGenerator/Mapper/EventPublisher 便捷 API 及显式注入版本。普通 CreateScope/CreateAsyncScope 仍是普通依赖注入：不激活 NOF，不承担分析器规定的协议。仅解析服务不会全局绑定它。

HTTP UseNOF、操作执行、可靠消息作用域及 NOF 测试作用域自动绑定并恢复自己的语义 Context 和已注册便捷服务。绑定跟随当前异步执行流，支持嵌套/await，成功或异常均恢复调用方；不同宿主/任务互不泄漏。内层宿主缺少的服务会遮蔽外层值；使用不可用的便捷功能时抛出明确 InvalidOperationException。

独立执行时用一个同步绑定作用域包住工作：

```csharp
await using var scope = services.CreateAsyncScope();
using var binding = scope.ServiceProvider.BindNOF(context);

var id = ConfigNodeId.New();
var rows = query.ProjectTo<ConfigNodeDto>();
domainEvent.PublishAsEvent();
```

BindNOF 不创建/释放 DI scope，不启动 worker，不发布事件。先释放绑定，再释放服务作用域。服务在首次使用便捷 API 时，按当时的实际 Context 延迟解析；未使用的服务不会在调用策略之前被构造。工厂异常继续传播，释放绑定后恢复调用方。Context 保持不可变；调用阶段仍可使用规范化视图，HTTP 不把业务 Context 回传给响应映射。

实现组合现有 PushCurrent 作用域，不再提供 daemon 标记、激活注册表或强制创建 scope 后立即激活的协议。只使用某个包时，仍可直接使用对应 IdGenerator/Mapper/EventPublisher.PushCurrent，无须增加一层依赖。PushCurrent(null) 在释放前显式遮蔽原服务。BindNOF 属于 Application，只绑定当前已注册的服务。

PublishAsEvent 保留同步等待发布的时机与异常语义，不改成记录事件/延迟派发，不改变工作单元。已有异步执行位置继续优先 await publisher.PublishAsync。

破坏性迁移：删除 ResolveDaemonServices、IDaemonService 实现及 ambient daemon 注册。NOF040 与修复已删除。独立激活改为 using var binding = services.BindNOF(context)，框架管理入口无需额外调用。自定义作用域资源使用普通 DI 生命周期/释放，不再使用标记接口；宿主和测试辅助一起升级。
