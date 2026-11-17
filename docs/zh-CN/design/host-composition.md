# 宿主装配与身份职责

[English](../../en/design/host-composition.md) · [开发指南](../README.md)

NOF 负责应用操作、业务授权、执行上下文与事务消息语义。原生宿主负责认证协议、遥测及具体基础设施选择。
AddNOF 安装统一执行器、默认执行引擎、诊断埋点与通用授权/租户 Filter；不再默认选择缓存、对象存储、
Backplane、ID 算法、消息传输、数据库、JWT 验证器或遥测导出器。

HTTP 的 AddNOF 重载还会注册适配器自身需要的 JSON、ProblemDetails 和 OpenAPI。宿主无需重复调用 AddProblemDetails()；
需要自定义时，配置原生 ProblemDetailsOptions 即可。

## 显式选择能力

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.Services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "app:{tenantId}:")
    .AddMemoryCacheService();
// 或 .AddRedisCache(connectionString)
builder.Services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "objects");
// 或 .AddMemoryObjectStorage() / .AddAmazonS3ObjectStorage(...)
builder.Services.AddBackplane().AddMemoryBackplane();
// 或 .AddRedisBackplane(...) / .AddRabbitMQBackplane()
builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
```

消息端点在独立 Scope 内恢复 Context，直接执行操作及正常权限/Filter 管道。NOF 不再提供 Inbox 存储、消费去重或消费重试调度。业务负责幂等和事务/保存边界，RabbitMQ 在操作完成后 ACK；原生适配器负责重投和死信。Memory 等待执行并传播失败。只有 AddOutbox 开启持久化发件及 Worker，队列映射不安装持久化。见[直接端点与迁移](transactional-outbox.md)。

CacheServiceSelector.ConfigureCacheServiceOptions 追加原生 CacheServiceOptions 配置并返回同一个 Selector，可在选择 Provider 前后使用，也可重复调用；回调按注册顺序执行，配置选项不会替换缓存 Provider。

SnowflakeIdGenerator 与 SnowflakeIdGeneratorOptions 移到 Infrastructure。Domain 只保留 IIdGenerator 和可空的
IdGenerator.Current；未绑定时返回 null，调用方决定跳过、选择替代实现或抛出异常。生成的 New() 必须取得 ID，
因此在这个调用点报告缺失的 Provider，New(IIdGenerator) 重载继续保留。Domain 没有运行时注册职责，AddNOFDomain
随之删除。AddNOFTest 为测试便捷能力显式选择内存缓存、对象存储与 Snowflake。

NOF 保留 ActivitySource/Meter 埋点，宿主使用原生 AddOpenTelemetry 配置源、指标与导出器。框架不安装日志导出器，
也不根据 OTEL_EXPORTER_OTLP_ENDPOINT 自动启用导出。Sample 中提供显式的宿主配置。

## 原生 HTTP 身份

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://identity.example.com/";
        options.Audience = "orders";
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseNOF();
app.MapGet<OrderService.Read>("/orders/{id}").RequireAuthorization();
```

JwtBearer 验证签名、发行者、受众与有效期，并负责发现文档、签名密钥刷新。原生端点授权负责 Challenge/Forbid。
HTTP 适配器重置作用域 IUserContext，只复制 HttpContext.User；不再解释原始 Authorization 头或 Context.Items 凭据。
未配置原生方案时，NOF 的业务拒绝仍可生成 Problem 响应，但不会伪造 WWW-Authenticate 头。

本地调用把调用方作用域的身份复制到隔离作用域，持久化消息不保存用户身份。通用授权与租户信任检查仍在所有来源上、
构造 Handler 之前执行。各宿主在自己的入口建立可信身份。

UseHostTenantAttribute、HostTenantInvocationFilter 与 OperationInspector.RequiresHostTenant 全部删除。
不再通过方法或魔术元数据键绕过租户策略、隐式切到宿主租户。入口显式选择 Context，或通过 Global/Service/UseCase
规则安装应用自有 Filter；它与授权的先后顺序是明确的应用策略。RequireAuthenticatedTenant 不再有平台操作例外，
独立的平台策略由应用 Filter 表达。缺失租户的普通规范化规则保持不变。

## 每个宿主自己的 JSON 配置

```csharp
builder.Services.AddNOFJson(options =>
    options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
```

框架负载使用宿主自己的 IOptions<JsonSerializerOptions>；在能力注册前后配置均有效，重复默认注册不会覆盖调用方配置。
每个 JsonObjectSerializer 捕获独立快照。HTTP 的 JsonOptions 由 ASP.NET Core 及原生配置顺序管理。不可变的源生成元数据
可以共享，但不存在进程级可变配置，也不存在必须抢在首次序列化前完成全进程配置的时限。

JsonSerializerOptions.NOF 和 ConfigureNOFJsonSerializerOptions 已删除。独立代码使用
JsonSerializerOptions.CreateNOF() 获得新实例，不影响 DI 或其他宿主。IObjectSerializer 保留为缓存与消息负载使用的薄接口，
用于字节与 JSON 元数据边界以及禁用反射的执行。应用负载应注册源生成上下文，并插入到反射回退之前；只有运行时明确开启
反射时才默认提供反射回退。

### 可选的 long 与 enum 字符串传输

默认行为不变：`long` 与 enum 使用数字 JSON。两个能力可以独立启用，在构建宿主前配置：

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.ConfigureNOFJson(json => json
    .UseStringInt64()
    .UseStringEnums());
```

`ConfigureNOFJson` 把同一回调应用到 NOF 负载选项和原生 HTTP JSON 选项，也可用于注册应用的 JSON 上下文。HTTP 请求体、响应、路由/查询/Header 绑定和 OpenAPI 使用 HTTP 选项；缓存、Backplane 和 Outbox 负载使用 NOF 选项。需要不同策略时，仍可分别使用 `AddNOFJson` 和 `ConfigureHttpJsonOptions`。独立代码可在 `JsonSerializerOptions.CreateNOF()` 上调用这些扩展。

`UseStringInt64()` 仅将有符号 `long` 写为不受区域设置影响的十进制字符串，包括可空值、集合元素以及生成的 long 值对象。例如 `9223372036854775807` 写为 `"9223372036854775807"`，避免 JavaScript 丢失精度。读取同时接受 Int64 范围内的十进制字符串和整数 token；小数、指数字符串和溢出值会失败。包括 `ulong` 在内的其他数字类型不变。OpenAPI 描述字符串表示并移除整数格式；生成的值对象通过转换器的底层类型元数据获得相同描述。

`UseStringEnums(namingPolicy: null, allowIntegerValues: false)` 使用原生枚举名称、`JsonStringEnumMemberName` 特性和 Flags 处理。传入 `JsonNamingPolicy.CamelCase` 等命名策略可调整名称。默认拒绝数字值和未命名的枚举值，包括路由/查询/Header 中的数字文本；契约确实需要数字回退时显式设置 `allowIntegerValues: true`。OpenAPI 使用原生枚举元数据，保留传输名称和可空/集合结构，不根据 CLR 成员重新拼接名称。

Native AOT 应为每种枚举调用 `UseStringEnums<OrderState>()` 替代全局调用，并为应用负载及底层枚举注册源生成 JSON 元数据。全局重载需要运行时代码生成；泛型重载和 `UseStringInt64()` 不需要。也可通过原生 System.Text.Json API 使用上下文本身的字符串枚举策略。

启用字符串传输会改变传输契约，应同步更新消费者，尤其是已有缓存和持久化负载的读取方。long 读取器兼容旧数字负载；严格的字符串枚举读取器拒绝旧数字枚举负载，除非显式启用数字回退。这两个选项不改变 CLR 类型或数据库存储。

## OIDC：接入外部身份提供者

NOF 提供业务鉴权，不承担身份协议和用户存储。Sample AppHost 运行官方 Keycloak 容器并导入 `Realms/nof-realm.json`。应用通过原生 ASP.NET Core Cookie/OpenIdConnect 完成浏览器登录，通过 JwtBearer 接受 API Bearer 令牌。Issuer、Audience、签名和有效期由原生认证处理器验证；Development 之外必须使用 HTTPS 元数据。

Keycloak 签发 `entitlements` 权限声明与 `nof.tenant_id` 租户声明。NOF 使用已验证身份检查 Contract 上的权限声明，UI AuthGuard 直接检查权限标识，不需要注册框架权限目录。账号、同意、会话、凭据和协议升级均由身份提供者负责。测试账号与权限场景见[能力工作台](sample-workbench.md)。

Sample 不再引用 OpenIddict，也不再提供授权、令牌、UserInfo 服务端点。旧的协议管理与 OAuth 链路演示页面已移除。更换身份提供者时，使用宿主原生 API 配置 Authority、ClientId、ClientSecret 和 API Audience。仓库中的 Realm 和客户端凭据仅用于本地演示，不能用于部署。

## 迁移

把原先的顶层 Provider 注册改为 Selector 链，显式注册所需可选能力，把遥测配置移到宿主。删除 AddNOFDomain 调用，
Snowflake 改为 NOF.Infrastructure 命名空间；检查 IdGenerator.Current 的可空处理。JWT 改为原生 AddAuthentication/AddJwtBearer
及对应中间件顺序，移除 Context.Items 凭据与宿主租户注解；特殊租户选择由应用显式实现。

历史 `20261114200000_UseOpenIddict` 迁移保留在链内。`20261117000000_ExternalIdentityProvider` 将这些实体移出活动 EF 模型，不删除物理身份表，也不改写历史凭据；业务与 Outbox 数据保留。升级前备份数据库、审查外部提供者的账号和权限配置，并重新登录，不自动转换凭据或令牌。以后清理旧身份表需要单独审查保留策略。启动本地 AppHost 会通过 MigrateOnStart 应用开发数据库迁移。

权限检查由 NOF.Abstraction 提供，Application 解析 Contract 声明，UI 无须引用 Contract。AddNOF/AddNOFUI 装配默认检查器与 Claim 授予来源，业务通过 IPermissionGrantProvider 查询当前授予，无需注册框架权限字典。权限仅声明在 Contract 接口/方法上，UI 直接按标识检查。见[授权指南](authorization.md)。

JSON Context、可直接发送的投递参数与链式注册参见[JSON 执行上下文](execution-context.md)。
