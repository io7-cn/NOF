# 统一操作、消息绑定与聚合集合

[English](../../en/design/unified-operations.md) · [统一调用扩展](invocation-extensions.md) · [持久化边界](persistence-boundaries.md)

HTTP、进程内调用和异步消费统一使用 ApplicationService。CommandHandler、NotificationHandler、专属 invoker、注册生成器和注册表已移除。领域内存事件保留当前作用域内的事件机制。

## 契约与实现

```csharp
// Contract：普通契约不需要 partial。
public interface IMailService : IApplicationService
{
    void Send(SendMail request);
}

// Application：操作实现入口仍然属于 ApplicationService。
public partial class MailService : ApplicationService<IMailService>;
public sealed class SendMailHandler(IMailGateway gateway) : MailService.Send
{
    public override async Task HandleAsync(
        SendMail request, InvocationContext invocation, CancellationToken ct)
    {
        await gateway.SendAsync(request, ct);
    }
}
```

Contract 不生成代码；NOF.Contract.SourceGenerator 保留声明分析器。Application 生成 Service.Operation 和完整强类型 Descriptor，删除 ServiceOperationReference、Descriptor.Reference。远程客户端使用共享负载及自行维护的集成端口。契约接口和 IApplicationServiceImplementation 无需 partial；Result 与失败分支生成在 Application 操作中。身份保持为契约加方法。注册时必须捕获静态 HandlerMappings；解析不再构造服务容器或回退到实例映射。

注册应用程序集和服务容器后，操作实现自动发现，无须逐个注册：

```csharp
builder.AddApplicationPart(typeof(MailService).Assembly);
builder.AddApplicationService<MailService>();
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq")!));
builder.Services.ConfigureRabbitMQ(endpoints =>
{
    endpoints.MapQueue<MailService.Send>("mail.send.v1");
    endpoints.MapQueue<OrderAudit.Record>("orders.audit.v1");
    endpoints.MapQueue<OrderProjection.Apply>("orders.projection.v1");
});
```

RabbitMQ 仅映射已有 Queue，Exchange 拓扑和发送由原生 API 处理；Memory 使用独立端点 API；泛型写法选择生成的操作描述符，显式重载只接受 OperationDescriptor<T>，注册时只接受 void 且无业务 FailureCase 的操作，不能静默丢弃 DTO 或 FailureCase union。相同消息可以绑定不同操作。每个订阅拥有独立的收件记录、去重键和重试；同一订阅的多个实例竞争消费，不同订阅各收到一份。订阅标识应带业务命名空间并保持稳定，不再隐式拼接部署服务名。

## 调用与可靠意图

InvokeAsync 等待应用操作完成。即时远程调用使用具体客户端；事务性工作通过 IOutbox.EnqueueAsync(message, context, options, ct) 暂存，再保存同一 IUnitOfWork，返回的是意图 ID，不是远程业务结果。基础设施注册稳定消息标识/版本与 OutboxHandler<T>，Outbox 不认识队列或服务操作。

```csharp
await memoryClient.SendAsync("mail.send.v1", mail, context, ct);
await outbox.EnqueueAsync(mail, context, cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

消息端点属于具体传输适配器，通用 MessageBinding 和生产者 RouteToQueue/Topic 声明已移除。同一操作可以接多个明确队列；具体客户端显式指定物理目标。注册、强类型适配器、重试与部署迁移见[通用事务性发件箱](transactional-outbox.md)。

## 一个执行边界

消息端点在独立 Scope 内恢复 Context，直接执行操作及正常权限/Filter 管道。NOF 不再提供 Inbox 存储、消费去重或消费重试调度。业务负责幂等和事务/保存边界，RabbitMQ 在操作完成后 ACK；原生适配器负责重投和死信。Memory 等待执行并传播失败。只有 AddOutbox 开启持久化发件及 Worker，队列映射不安装持久化。见[直接端点与迁移](transactional-outbox.md)。

Application 中的 InvocationContext 组合语义 Context，提供 Operation、InvocationId、Origin（Local/Http/Message）、实现策略元数据。过滤器通过 WithContext/WithTenantId 替换语义信息，视图保留调用身份，结果通过返回值传播。Context.Current 保存语义 Context；向嵌套语义 API 传递 invocation.Context。身份唯一来源仍是作用域内的 IUserContext。

所有入口检查仅声明在 Contract 接口/操作方法上的权限，Handler 不再声明权限。消息入口不会反序列化生产者身份或访问令牌；宿主在授权前通过所选管道/过滤器建立消费者身份，匿名消费者不能调用受保护操作。宿主应限制消息代理入口和租户头的信任边界。严格租户策略适用于所有操作来源。

## 破坏性迁移与积压消息

TenantAuthorizationOptions.RequireAuthenticatedTenantForRequests 和 RejectMismatchedTenantForRequests 分别更名为 RequireAuthenticatedTenant 和 RejectMismatchedTenant，策略现在适用于所有操作来源。

旧 Command/Notification Handler 改成返回 void 的单向操作与明确入站 Queue/Subscription 绑定，旧上下文子类改用 InvocationContext＋Origin。即时调用使用具体客户端；延迟 API 改为 IOutbox.EnqueueAsync 加选项。旧分派器与出站路由 API 不保留兼容层。

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

RabbitMQ 仅将操作映射到已有队列，参见[原生拓扑示例](rabbitmq-sample.md)。

独立 OperationHandler 类无需 Contract 方法即可使用同一执行模型，参见[内部反应与 Executor 迁移](one-way-operations.md#application-内部反应)。
