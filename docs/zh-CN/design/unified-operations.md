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

## 调用与可靠意图

InvokeAsync 等待应用操作完成。即时远程调用使用具体客户端；事务性工作通过 IOutbox.EnqueueAsync(message, context, options, ct) 暂存，再保存同一 IUnitOfWork，返回的是意图 ID，不是远程业务结果。基础设施注册稳定消息标识/版本与 IOutboxHandler<T>，Outbox 不认识队列或服务操作。

```csharp
await MailService.Send.InvokeAsync(services, mail, context, ct);
await memoryClient.SendAsync("mail.send.v1", mail, context, ct);
await outbox.EnqueueAsync(mail, context, cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

消息绑定只负责入站，保留 Queue/Subscription.To，移除生产者 RouteToQueue/Topic 声明。同一操作可以接多个明确队列；具体客户端显式指定物理目标。注册、强类型适配器、重试与部署迁移见[通用事务性发件箱](transactional-outbox.md)。

## 一个执行边界

IOperationExecutor 在宿主现有作用域内执行已确定的操作并等待完成。IOperationInvoker 为本地调用创建新作用域并复制已验证身份；HTTP 复用请求作用域；Inbox 复用既有事务作用域，使业务修改、处理进度和新增 Outbox 一同提交。过滤器执行前不会构造 Handler。

Application 中的 InvocationContext 组合语义 Context，提供 Operation、InvocationId、Origin（Local/Http/Message）、实现策略元数据和 ServiceExecution。过滤器通过 WithContext/WithTenantId 替换语义信息，各视图共享执行结果。Context.Current 保存语义 Context；向嵌套语义 API 传递 invocation.Context。身份唯一来源仍是作用域内的 IUserContext。

所有入口执行相同的契约与实现授权策略。消息入口不会反序列化生产者身份或访问令牌；宿主在授权前通过所选管道/过滤器建立消费者身份，匿名消费者不能调用受保护操作。宿主应限制消息代理入口和租户头的信任边界。严格租户策略适用于所有操作来源。

HTTP 状态码和认证挑战头由 HttpRequestInboundAdapter 处理；RejectionReason 用 Policy/Authentication/Authorization 表达原因，执行上下文不携带 HTTP 状态码。策略拒绝与已完成的业务失败 union 区分。技术异常继续传播；Inbox 将策略拒绝记录为终止失败，技术故障按既有策略重试。消息代理在可靠接受收件记录后确认，不等待业务执行完成。

内存和 RabbitMQ 传输共用 MessageInboxReceiver：先建立投递租户再解析持久化依赖；数据库冲突只有在确认“消息 ID＋消费者标识”的记录确实存在后，才算重复投递已接受。

## 破坏性迁移与积压消息

TenantAuthorizationOptions.RequireAuthenticatedTenantForRequests 和 RejectMismatchedTenantForRequests 分别更名为 RequireAuthenticatedTenant 和 RejectMismatchedTenant，策略现在适用于所有操作来源。

旧 Command/Notification Handler 改成返回 Empty 的操作与明确入站 Queue/Subscription 绑定，旧上下文子类改用 InvocationContext＋Origin。即时调用使用具体客户端；延迟 API 改为 IOutbox.EnqueueAsync 加选项。旧分派器与出站路由 API 不保留兼容层。

通用 Outbox 必须在排空旧记录后执行结构迁移，按[发件箱迁移步骤](transactional-outbox.md)部署。此前 Inbox/Broker 路由名变更仍需排空，或为所有租户显式映射旧记录、订阅与顺序状态，备份并保留去重/顺序进度，不猜测未解析路由。

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
