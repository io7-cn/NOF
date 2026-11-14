# 宿主装配与身份职责

[English](../../en/design/host-composition.md) · [开发指南](../README.md)

NOF 负责应用操作、业务授权、执行上下文与事务消息语义。原生宿主负责认证协议、遥测及具体基础设施选择。
AddNOF 安装执行器、调用器、默认执行引擎、诊断埋点与通用授权/租户 Filter；不再默认选择缓存、对象存储、
Backplane、ID 算法、消息传输、数据库、JWT 验证器或遥测导出器。

## 显式选择能力

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.Services.AddCacheService(options => options.KeyPrefix = "app:{tenantId}:")
    .AddMemoryCacheService();
// 或 .AddRedisCache(connectionString)
builder.Services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "objects");
// 或 .AddMemoryObjectStorage() / .AddAmazonS3ObjectStorage(...)
builder.Services.AddBackplane().AddMemoryBackplane();
// 或 .AddRedisBackplane(...) / .AddRabbitMQBackplane()
builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
```

入口注册公共策略，类型化 Selector 选择 Provider 并保留完整方法名。重复调用入口不会重复注册共享服务，
自定义实现仍可直接使用 DI。选择缓存不会顺带启动传输或持久化 Worker。AddMemoryMessaging 开启内存消息；
AddInbox/AddOutbox 按需安装消息专用内存持久化。使用 MessageDelivery 的原生 RabbitMQ 发布适配器显式注册其依赖。
EF/NHibernate 依然是独立选择。

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

UseHostTenantAttribute、HostTenantInvocationFilter 与 ServiceOperationInspector.RequiresHostTenant 全部删除。
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

## OIDC 交给现成协议实现

NOF 不再维护 OAuth/OIDC Server，也不为其增加新的包装包。三个 OidcServer 包、端点接口、协议模型、密钥轮换、令牌仓储
和 ORM 集成均已删除。宿主直接接入现有身份服务，或直接使用 OpenIddict。
[OpenIddict 官方说明](https://documentation.openiddict.com/introduction)明确把协议与原生存储交给库，把登录、同意和应用授权留给宿主。

Sample 直接引用 OpenIddict.Server.AspNetCore、OpenIddict.EntityFrameworkCore，API 使用 JwtBearer。演示覆盖授权码与 PKCE、
客户端凭据、刷新、设备授权、发现/JWKS、内省/吊销、UserInfo 和 Token Exchange。Identity 模块只包含演示用户/同意决策与
客户端初始化，是应用代码。OAuth 演示管理端点与固定用户批准仅在 Development 开启；实际部署应提供自己的登录/同意策略
和持久化签名、加密凭据，不能使用演示决策或开发证书作为生产配置。

Identity 并入现有 ConfigurationDbContext、数据库连接与迁移链，新增四张原生表。原生实体明确关闭业务软删除约定，
保留上游吊销/清理语义。既有数据库迁移生命周期完成后才初始化演示客户端。DatabasePerTenant 共用迁移链，身份请求使用宿主
上下文，应用的租户路由仍须明确配置。

Token Exchange 保留上游 Presenter/Audience 限制。演示通过明确的 delegate_to 指定已注册、具备交换权限的接收客户端，
将其写入批准的 Resources，只允许 Subject 与 Actor 共同拥有的 Scope。UI 获取用户令牌时传入 RecipientClientId；这是演示
同意策略，不是允许任意客户端交换窃取的令牌。JwtBearer 离线验证 JWT，内省能观察吊销；需要 API 即时观察吊销时，应由宿主
选择在线验证策略，否则访问令牌的接受窗口由有效期约束。客户端管理直接使用原生 ApplicationManager，Sample 不提供动态
注册协议端点。

本仓通过真实原生协议与 SQLite 集成测试验证所选 OpenIddict 版本在 .NET 11 下的行为；上游支持表目前明确列到 .NET 10。
框架禁用反射的 Smoke 测试不代表所有第三方身份服务都支持 Native AOT。

## 迁移

把原先的顶层 Provider 注册改为 Selector 链，显式注册所需可选能力，把遥测配置移到宿主。删除 AddNOFDomain 调用，
Snowflake 改为 NOF.Infrastructure 命名空间；检查 IdGenerator.Current 的可空处理。JWT 改为原生 AddAuthentication/AddJwtBearer
及对应中间件顺序，移除 Context.Items 凭据与宿主租户注解；特殊租户选择由应用显式实现。

20261114200000_UseOpenIddict 接在 RemoveInboxClassification 之后，仅创建四张原生身份表及索引。旧 OAuthClient、
PersistedSigningKey、RevokedRefreshToken 表保留在活动模型之外，不修改业务与消息的负载、Headers、ID、租约或去重历史。
不能把旧哈希、加密密钥、授权码或刷新令牌直接解释为 OpenIddict 记录。停止旧程序、备份各数据库后，审查新客户端和权限配置、
凭据轮换，并重新认证，或另行设计并审查明确的过渡方案。Down 要求恢复匹配备份和包，不猜测反向转换。本轮源码验证不升级真实数据库。
