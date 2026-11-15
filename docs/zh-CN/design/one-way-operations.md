# 单向操作与统一 Inbox 基座

[English](../../en/design/one-way-operations.md) · [统一操作](unified-operations.md) · [事务发件箱](transactional-outbox.md)

Contract 返回类型表达是否具有业务回信能力，而不只是有无有效载荷。

| Contract | Handler | 面向生产者的业务回信 |
|---|---|---|
| void Send(Message) | Task HandleAsync(...) | 没有，明确单向 |
| Empty Update(Message) | Task<Empty>，声明 FailureCase 时为 Task<Result> | 无载荷成功或已声明失败 |
| Dto Read(Query) | Task<Dto>，声明 FailureCase 时为 Task<Result> | 成功数据或已声明失败 |

```csharp
public interface IMailService : IApplicationService
{
    void Send(SendMail request);
    [FailureCase("Denied")]
    Empty ChangeSettings(ChangeMailSettings request);
}

public partial class MailService : ApplicationService<IMailService>;
public sealed class SendMailHandler(IMailGateway gateway) : MailService.Send
{
    public override Task HandleAsync(SendMail request, InvocationContext invocation, CancellationToken ct)
        => gateway.SendAsync(request, ct);
}
```

void 生成 OperationHandler<TRequest> 与 OperationDescriptor<TRequest>，响应元数据是 typeof(void)，没有业务结果分支、Result、失败分支或返回业务结果的 InvokeAsync。执行宿主仍等待 Handler Task 完成，以维持事务、重试和取消；不会生成 async void、忽略 Task 或自动 Task.Run。

void 标记 FailureCase 报 NOF220；旧 NOF209 禁止 void 的诊断和自动改成 Empty 的修复删除。请求/响应描述符不能伪装成 void，void 也不能伪装成 Empty。明确等待回信的 IOperationExecutor.ExecuteAsync 拒绝 void；宿主使用 OperationExecutor.ExecuteInScopeAsync 等待内部处理，不将处理记账返回给生产者。

## 接收与执行分开

```csharp
services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<MailService.Send>("mail.send.v1"));
services.AddMemoryMessaging(memory => memory.MapQueue<MailService.Send>("mail.send.v1"));
```

队列/订阅只接受单向 Handler：void 契约操作或独立的 OperationHandler<TRequest> 类。即便没有 FailureCase，Empty 仍是可回信操作，不能静默丢弃。具体客户端选择原生目的地，IOutbox 暂存集成意图。发送按客户端约定的接收条件完成，而不是等待消费者业务结果。Outbox 元数据保持重试身份，不承诺副作用恰好一次。

Inbox 先持久化收据，再允许 Broker 确认。Worker 随后等待完整 Handler Task，再保存完成并提交既有事务。策略/Filter 拒绝记录为终止失败，技术故障回滚并按策略重试。消费者失败通过收据状态、日志或业务自有的后续消息观察，不沿原发送通道回信。取消仍是宿主处理信号。

## 统一 Inbox 执行

IInboxHandler、OperationInboxHandler 删除。适配器只向宿主本地 InboxEndpointRegistry 登记 InboxEndpoint(Id, Operation) 元数据。Id 是稳定的不透明收据键，Inbox 不将其解释为 Queue、Exchange 或 Topic。Worker 解析描述符、反序列化声明的输入，在既有租户作用域和事务中调用同一个 OperationExecutor.ExecuteInScopeAsync。

基座保留持久化、精确收据去重、所有者围栏、恢复、清理和重试；适配器保留物理拓扑与传输确认。不引入另一个回调/Handler 接口。自定义存储仍实现 IInboxStore，自定义适配器可登记不透明端点并调用 MessageInboxReceiver，无需第二套业务处理器。

Memory 与 RabbitMQ 的可见队列名相同也可绑定不同操作，因为持久化键仍带适配器前缀。同一操作可以有多个入站收据键。重复键与非 void 目标配置失败；已有收据缺失映射时明确失败，不从载荷猜测 CLR 类型。

## HTTP 与迁移

普通 MapGet/MapPost 操作响应映射拒绝 void。需要 HTTP 接收单向工作时，使用原生接收端点检查提交者权限，持久化 Outbox 意图或可靠接收收据后，再返回 202 等传输确认。确认只表示接收，不将 Handler 当作请求/响应操作执行，也不返回它的失败。不能只启动一个进程内 Task 就返回 202。入口仍负责原生授权和租户选择；队列执行使用宿主建立的可信消费者身份，不持久化生产者 Token/Claims。

队列契约的 Empty 改为 void，Handler 的 Task<Empty> 改为 Task；已有 REST/本地 Empty 操作及其 FailureCase 结果保留。显式队列映射使用 OperationDescriptor<TRequest>，泛型 MapQueue<Operation> 写法不变。删除 keyed IInboxHandler 注册，常规适配器自动登记统一元数据。运行时、生成器、CodeFix、宿主与适配器同步升级。

单向操作/Inbox 调整本身不要求结构迁移；本批 Outbox 收敛另需审查并应用 SimplifyOutboxMetadata。保留消息/收据 ID、JSON 载荷和适配器前缀键，维持待处理工作与去重历史。过去自定义 Handler 的载荷现在必须匹配单向请求；不兼容时升级前先排空或显式迁移，不能将任意旧字节重新解释为新 DTO。

## Application 内部反应

ServiceOperation 改名为 OperationHandler。内部逻辑可直接声明普通具体类，不必增加 Contract 接口方法、partial 或 Attribute：

```csharp
public sealed class AwardWelcomePoints(IPoints points) : OperationHandler<UserCreated>
{
    public override Task HandleAsync(UserCreated message, InvocationContext context, CancellationToken ct)
        => points.AddAsync(message.UserId, 100, ct);
}

builder.AddApplicationPart(typeof(AwardWelcomePoints).Assembly);
builder.Services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<AwardWelcomePoints>("user.welcome-points"));
builder.Services.AddInvocationFilters(filters => filters.UseCase<AwardWelcomePoints>().Use<AuditFilter>());
```

Application 生成器自动登记 scoped Handler 和强类型描述符，不构造 Handler；这种写法不需要 AddApplicationService 或逐个注册。独立 OperationHandler<TRequest,TResponse> 也可映射 HTTP 或本地回信调用，支持可空响应和业务自有 Union。FailureCase 生成 Result 分支仍属于 Contract 方法能力。

独立操作以 Handler 的完整 CLR 名作为身份，相同输入/输出可有多个处理器。它没有公共 Contract，也不推断 Contract 权限；仍应用 Global、UseCase<Handler>() Filter 以及宿主租户和自定义授权约束。Service Filter 仅作用于 Contract 操作。自动发现不会自动公开 HTTP 路由；公开 HTTP 映射仍应按需求配置原生授权，内部反应应绑定可信消费入口。

可运行 Notifications 示例的 CountOrder 已采用这种形式，IMessageDemoService 不再声明它，直接 MapQueue<CountOrder>。持久化 Inbox 收据键独立于 Handler 身份，此次重构保留已有队列收据。

两个执行服务合并：IOperationExecutor 是原本地 Invoker 的新名字，OperationExecutor 是唯一实现。HTTP/Inbox 调用该实现的 ExecuteInScopeAsync 保留宿主作用域、身份和事务。本地 ExecuteAsync 则创建隔离回信作用域。删除 IOperationInvoker 和旧独立 Executor 接口，不保留兼容层。
