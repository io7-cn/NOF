# 框架边界与原生平台 API

[English](../../en/design/framework-simplification.md) · [宿主装配](host-composition.md) · [授权](authorization.md) · [持久化](persistence-boundaries.md)

NOF 专注业务操作的描述与执行。共享业务语义归入现有分层，传输、生命周期和技术异常使用宿主与 Provider 的原生能力；现有归属足够时，不新增包或转发服务。

## 权限能力归属

| 归属 | 职责 |
|---|---|
| Abstraction | PermissionMatch、IPermissionMetadata、不可变策略、IPermissionChecker 与默认 Claim 授予实现 |
| Contract | 仅在接口和操作方法上保留 RequirePermission、RequireAuthenticated、AllowAnonymous 声明 |
| Application | AuthorizationPolicyResolver 解释契约默认值及继承规则 |
| Infrastructure | 缓存调用策略，在租户信任校验后、构造 Handler 前执行权限检查 |
| UI | 通过 Abstraction 检查标识或显式 UI 策略，不读取 Contract 或 UseCase |

Contract 为共享权限模型引用 Abstraction；Abstraction 不依赖其他 NOF 运行时包，Domain 仍不依赖 Contract。UI 与 Hosting 在 NOF 运行时中也只引用 Abstraction。不再单独维护授权包或兼容转发层。

## 持久化异常

Application 保留 IUnitOfWork 与最小事务接口，不定义数据库异常。保存只提供调用方确实需要区分的标准语义：

| 情形 | 传播异常 |
|---|---|
| EF Core 乐观并发失败、NHibernate stale state | System.Data.DBConcurrencyException，InnerException 保留原异常 |
| EF Core 更新失败、NHibernate ADO 失败 | System.Data.DataException，InnerException 保留原异常 |
| 开始、提交、回滚事务失败 | 原生异常 |
| 取消、业务钩子或 Provider 的非预期异常 | 原异常 |

删除对 NOF.Application.DbException、DbUpdateException、DbUpdateConcurrencyException、DbTransactionException、DbTransactionCommitException 的引用与捕获。确需跨 Provider 区分保存错误时使用上述 BCL 异常；事务诊断和取消保持原样。

Inbox 仅对 DataException 在新作用域中复核，确认同一消息 ID 与消费者收据已持久化后才允许确认。取消和编程错误不进入重复确认路径。所有者围栏与事务边界不变，自定义 Store 遵循相同的保存错误约定。

## 删除的重复入口

| 已删除 API | 替代方式 |
|---|---|
| ReplaceOrAdd 与 ReplaceOrAddSingleton/Scoped/Transient | Microsoft.Extensions.DependencyInjection.Extensions 中原生 services.Replace(ServiceDescriptor.Singleton/Scoped/Transient(...)) |
| DelegateBackgroundService 与委托 AddHostedService 重载 | 普通 BackgroundService/IHostedService 加原生 AddHostedService<T> |
| HttpRequestInboundAdapter | 现有 HttpContext.InvokeAsync 与 Fluent HTTP 端点直接调用 IOperationExecutor |
| ClaimsPrincipal.HasPermission | IPermissionChecker，统一目录校验和已认证授予语义 |
| ClaimsPrincipal.GetIdentity/GetIdentities | 原生 Identities.OfType<T>() 查询 |
| JwtClaimsIdentity | 原生宿主认证与 ClaimsIdentity，不再提供将未验证 Token 声明标为已认证的解析包装 |
| 空 NOFApplicationConstants 与未使用的服务方法反射辅助 | 现有具体常量与 OperationDescriptor.MethodInfo |

事件注册生成器也改为生成原生 Replace；Provider Selector 保留后注册覆盖行为。HTTP 在计算语义上下文前复制原生身份，显式端点与 Fluent 端点继续走统一授权边界。Hosting 不再依赖 JWT 库，协议依赖由宿主选择。

Scoped Lazy<T>、显式能力 Selector、ApplicationPart 发现、强类型业务失败、调用 Filter、最小持久化端口和可靠 Inbox/Outbox 仍有具体调用或框架语义，予以保留。OutboxPermanentFailureException 仍表达终止投递；换成无法区分的普通异常会丢失重试决策。

运行时、Provider 与生成器需同步更新。共享权限类型改用 NOF.Abstraction，AuthorizationPolicyResolver 改用 NOF.Application。这批 API 调整不需要数据库迁移，也不重新解释已存消息。

权限目录与展示 DTO 归业务所有，AddPermissions、PermissionCatalog 和 PermissionDefinition 删除；缓存执行计划不缓存授予结果，每次检查都调用来源查询当前数据。Outbox 自动保存并还原完整 JSON 标量上下文，直接提供可发送的 Headers；MessageDelivery 及其注册删除。

JSON Context、可直接发送的投递参数与链式注册参见[JSON 执行上下文](execution-context.md)。
