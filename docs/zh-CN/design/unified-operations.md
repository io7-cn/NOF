# 统一操作、消息绑定与聚合集合

[English](../../en/design/unified-operations.md) · [统一调用扩展](invocation-extensions.md) · [持久化边界](persistence-boundaries.md)

HTTP、进程内调用和异步消费统一使用 ApplicationService。CommandHandler、NotificationHandler、专属 invoker、注册生成器和注册表已移除。领域内存事件保留当前作用域内的事件机制。

## 契约与实现

```csharp
// Contract：普通契约不需要 partial。
public interface IMailService : IApplicationService
{
    Empty Send(SendMail request);
}

// Application：操作实现入口仍然属于 ApplicationService。
public partial class MailService : ApplicationService<IMailService>;
public sealed class SendMailHandler(IMailGateway gateway) : MailService.Send
{
    public override async Task<Empty> HandleAsync(
        SendMail request, InvocationContext invocation, CancellationToken ct)
    {
        await gateway.SendAsync(request, ct);
        return Empty.Instance;
    }
}
```

Contract 不再生成代码；NOF.Contract.SourceGenerator 保留原名，只承载声明分析器。ServiceOperationReference 归 Application，由 Application 生成 MailService.Send 及完整 Descriptor，其 Reference 保留稳定操作身份。远端生产者只引用共享业务 Contract 和框架 Application，显式构造操作引用，无需依赖接收方实现。所有契约接口（包括 FailureCase 声明）和 IApplicationServiceImplementation 均无需 partial；Result 与失败分支直接生成在 Application 操作中。操作身份是“契约＋方法”，不依赖请求/响应组合或 Handler 实现类名。

注册应用程序集和服务容器后，操作实现自动发现，无须逐个注册：

```csharp
builder.AddApplicationPart(typeof(MailService).Assembly);
builder.AddApplicationService<MailService>();
builder.Services.AddMessageBindings(bindings =>
{
    bindings.Queue<SendMail>("mail.send.v1").To(MailService.Send.Descriptor);
    bindings.Subscription<OrderCreated>("orders.created.v1", "orders-audit")
        .To(OrderAudit.Record.Descriptor);
    bindings.Subscription<OrderCreated>("orders.created.v1", "orders-projection")
        .To(OrderProjection.Apply.Descriptor);
});
```

Queue/Subscription.To 只接受 ServiceOperationDescriptor<T, Empty>，不能静默丢弃 DTO 或 FailureCase union。相同消息可以绑定不同操作。每个订阅拥有独立的收件记录、去重键、顺序进度和重试；同一订阅的多个实例竞争消费，不同订阅各收到一份。订阅标识应带业务命名空间并保持稳定，不再隐式拼接部署服务名。

## 调用与投递

```csharp
// 等待业务完成，创建隔离的本地执行作用域。
await MailService.Send.InvokeAsync(services, mail, context, ct);

// IMessageDispatcher：等待接受投递，不代表业务已经执行。
await messages.SendAsync(MailService.Send.Descriptor.Reference, mail, context, ct);
await messages.PublishAsync(new OrderCreated(orderId), context, ct);

// 暂存到当前事务，随后与业务修改一同提交。
await messages.DeferSendAsync(MailService.Send.Descriptor.Reference, mail, context, ct);
await messages.DeferPublishAsync(new OrderCreated(orderId), context, ct);
await unitOfWork.SaveChangesAsync(ct);
```

有序版本依次接收操作描述符（仅发送）、消息、orderKey、Context、可选 completesOrderKey 和取消令牌，原有连续序号约束不变。可靠接受不等于外部业务副作用恰好执行一次。

没有本地消费者的生产者只配置出站路由：

```csharp
services.AddMessageBindings(bindings =>
{
    bindings.RouteToQueue(new ServiceOperationReference<SendMail>(
        typeof(IMailService), nameof(IMailService.Send)), "mail.send.v1");
    bindings.Topic<OrderCreated>("orders.created.v1");
});
```

这些声明不会启动本地消费者。收发双方约定稳定路由和载荷格式，不再持久化 CLR 消息类型名作为发布路由。注册表属于 DI，首次使用后冻结，并拒绝身份冲突。

## 一个执行边界

IOperationExecutor 在宿主现有作用域内执行已确定的操作并等待完成。IOperationInvoker 为本地调用创建新作用域并复制已验证身份；HTTP 复用请求作用域；Inbox 复用既有事务作用域，使业务修改、处理进度和新增 Outbox 一同提交。过滤器执行前不会构造 Handler。

Application 中的 InvocationContext 组合语义 Context，提供 Operation、InvocationId、Origin（Local/Http/Message）、实现策略元数据和 ServiceExecution。过滤器通过 WithContext/WithTenantId 替换语义信息，各视图共享执行结果。Context.Current 保存语义 Context；向嵌套语义 API 传递 invocation.Context。身份唯一来源仍是作用域内的 IUserContext。

所有入口执行相同的契约与实现授权策略。消息入口不会反序列化生产者身份或访问令牌；宿主在授权前通过所选管道/过滤器建立消费者身份，匿名消费者不能调用受保护操作。宿主应限制消息代理入口和租户头的信任边界。严格租户策略适用于所有操作来源。

HTTP 状态码和认证挑战头由 HttpRequestInboundAdapter 处理；RejectionReason 用 Policy/Authentication/Authorization 表达原因，执行上下文不携带 HTTP 状态码。策略拒绝与已完成的业务失败 union 区分。技术异常继续传播；Inbox 将策略拒绝记录为终止失败，技术故障按既有策略重试。消息代理在可靠接受收件记录后确认，不等待业务执行完成。

内存和 RabbitMQ 传输共用 MessageInboxReceiver：先建立投递租户再解析持久化依赖；数据库冲突只有在确认“消息 ID＋消费者标识”的记录确实存在后，才算重复投递已接受。

## 破坏性迁移与积压消息

TenantAuthorizationOptions.RequireAuthenticatedTenantForRequests 和 RejectMismatchedTenantForRequests 分别更名为 RequireAuthenticatedTenant 和 RejectMismatchedTenant，策略现在适用于所有操作来源。

用 IMessageDispatcher 替换 ICommandSender/INotificationPublisher；将消息 Handler 改成返回 Empty 的服务操作，显式声明队列/订阅，并向 Send/DeferSend 传入操作描述符。用 InvocationContext＋Origin 替换三种入站上下文和 InvocationKind。复用作用域调用 IOperationExecutor，需要隔离作用域时调用 IOperationInvoker。删除的 API 不保留兼容适配器。

升级前必须处理旧路由记录。推荐停止生产者，由旧版本排空 Outbox、代理队列和 Inbox，确认没有待处理、占用中或仍需恢复的失败记录，再部署收发双方的新绑定并恢复生产。此路径不需要数据库结构迁移。

无法排空时，先停止全部工作器，编写应用自己的明确映射迁移：Outbox.DispatchRoutes 中的旧消息类型字符串改为稳定队列/主题；Inbox.Route 中的旧 Handler/队列名改为 queue:<queue> 或 subscription:<subscription>；同时迁移每个租户库的 NOFInboxOrderState.Route。包括保留的已处理记录以维持去重，处理键冲突并保留顺序进度，同时迁移或重放消息代理积压。备份并验证映射后再启动。NOF 不猜测旧 Handler 名的对应关系，也不会静默丢弃无法解析的收件记录。

## 聚合拥有的集合

```csharp
public partial class ConfigNode
{
    [ReadOnlyCollection]
    private readonly List<ConfigFile> _configFiles = [];

    [ReadOnlyCollection("Tags")]
    private readonly HashSet<ConfigTag> _tags = new(ReferenceEqualityComparer.Instance);
}

entity.OwnsMany(node => node.ConfigFiles);
entity.OwnsMany(node => node.Tags);
```

Domain 生成缓存的实时 IReadOnlyCollection 视图和 CollectionBackingField 关联元数据。包装不复制元素，不暴露 ICollection/List/HashSet 的修改接口；聚合业务方法修改私有字段。它保护集合成员关系，不限制元素自身的可变性。实体 HashSet 导航使用引用相等比较器。

首版支持顶层非泛型 partial class 中的 private readonly List<T>/HashSet<T> 字段。默认 _configFiles → ConfigFiles，可通过属性参数显式命名。NOF020 诊断不支持的字段、冲突或非法名称；NOF021 提示补充 partial 并提供代码修复。读取视图前必须初始化字段。

EF Core 在应用完成关系配置后，仅为已明确声明的 OwnsMany 自动配置字段访问，验证关联，并报告显式访问模式或字段映射冲突。Attribute 不建立关系。HasMany、跳跃导航和 NHibernate 的集合实例替换不在自动约定范围。ConfigNode 示例已经落地生成视图，保留强类型 OwnsMany。通用的危险 Mut 扩展已删除，外部应调用聚合方法。

[Application 结果归属及描述符迁移](expected-failures.md)。

[当前 Contract/Application/Abstraction 边界](contract-boundaries.md)。
