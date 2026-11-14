# 契约授权与权限目录

[English](../../en/design/authorization.md) · [宿主装配](host-composition.md)

NOF 负责权限声明、检查语义与执行拦截；原生认证验证凭证并建立可信 ClaimsPrincipal。NOF.Authorization 提供共享运行时，不引用 Application、Domain 或 ASP.NET Core。Contract 继续只含声明与数据模型，不依赖其他 NOF 运行时包。

## 权限只声明在 Contract 接口上

```csharp
[RequireAuthenticated]
public interface IConfigService : IApplicationService
{
    [RequirePermission("config.read")]
    ConfigDto Read(ReadConfigRequest request);

    [RequirePermission("config.write", "config.admin", Match = PermissionMatch.Any)]
    Empty Update(UpdateConfigRequest request);

    [AllowAnonymous]
    PublicConfigDto ReadPublic(ReadPublicConfigRequest request);
}
```

RequirePermission 必须包含至少一个非空权限标识，并隐含认证要求；Match 默认 All。同一声明上的多个权限组按 AND 组合。例如同时声明 `RequirePermission("config.read")` 和 `RequirePermission("owner", "admin", Match = PermissionMatch.Any)`，含义是 read AND (owner OR admin)。

操作方法声明替换接口默认要求，较近的接口/方法声明替换继承默认值。AllowAnonymous 取消该契约继承的默认要求，但不能绕过宿主认证或租户约束。没有声明的契约保持匿名访问。Handler、请求/消息类型与 UI 页面均不是权限声明来源。

NOF240 检查空组、空白标识与无效枚举；NOF241 检查同一成员上的匿名与认证/权限冲突；NOF242 拒绝实现方法上的声明。实现 IPermissionMetadata 的自定义 Attribute 也经过运行时校验。同层继承声明的权限组必须一致，不依赖反射枚举顺序；有歧义时配置失败，要求在派生契约明确声明。不再保留 MetadataAttribute、字符串键值协议或通用 IMetadata 标记。

## 权限定义与授予分开

```csharp
services.AddPermissions(
    new PermissionDefinition("config.read", "读取配置", "配置管理"),
    new PermissionDefinition("config.write", "修改配置", "配置管理"),
    new PermissionDefinition("config.admin", "管理配置", "配置管理"));
```

AddNOF 与 AddNOFUI 已注册默认检查服务，宿主只补充自己的定义。PermissionCatalog 按稳定顺序返回只读定义；标识按 OrdinalIgnoreCase 比较，重复标识报错，首次使用后冻结。不同 ServiceCollection 的目录互相独立。定义权限不会自动授予任何身份。

声明引用的所有标识（包括 Any 的每个备选项）都必须存在。默认执行器在启动服务前验证生成的操作注册，手工描述符则在执行前验证。未知标识属于配置错误，直接调用 IsGrantedAsync 也一样。应在描述端点、读取目录或启动宿主前注册定义。独立 UI 宿主显式装配同一组定义；框架不会安装权限/角色数据库或迁移。

## 检查权限与替换授予来源

```csharp
var granted = await permissions.IsGrantedAsync("config.read", ct);
```

IPermissionChecker 的便捷 API 使用当前 Scoped IUserContext 与语义 Context；CheckAsync 接收只读 AuthorizationPolicy、显式 ClaimsPrincipal 和 Context，返回 Granted、AuthenticationRequired 或 Denied。权限组被快照，修改 Attribute 或原始数组不会改变有效策略。

默认 ClaimsPermissionGrantProvider 只读取已认证 Identity 的权限 Claim，支持精确匹配与通配授予。OAuth scope、角色不会自动变成业务权限；附加一个未认证 Identity 不能向已认证主体注入权限。

需要从业务数据获得授予时，用普通 DI 注册 Scoped IPermissionGrantProvider。它接收主体、一个已定义权限、显式租户上下文和取消令牌。认证、Any/All 与拒绝处理继续由 NOF 完成。查询异常按技术异常传播，不会变成允许。本轮不提供角色或组织管理。

执行、失败描述与 OpenAPI 共享缓存后的契约策略。HTTP、本地调用和消息消费都在构造业务 Handler 前检查。租户信任检查先于授予服务的构造与查询。框架在显式的已认证租户上下文内延迟解析授予服务，确保 Scoped 持久化依赖捕获可信租户；匿名拒绝不构造授予服务。消息不恢复生产者 Claims/Token，由宿主显式建立可信消费者身份。自定义执行引擎继续负责落实自己的执行计划。

生成的操作初始化器为裁剪保留继承契约的方法。独立的裁剪调用方使用 AuthorizationPolicyResolver 时，也应保留所选契约的继承方法声明。

## UI 直接按权限控制展示

```razor
<AuthGuard Permission="config.write">
    <ChildContent><button>编辑</button></ChildContent>
    <UnauthorizedContent><span>只读</span></UnauthorizedContent>
</AuthGuard>
```

AuthGuard 使用共享检查器，复杂组合可传入 UI 自己的 Policy；不能同时指定 Policy 与 Permission。两者都不指定时要求认证。路由包装接收显式 Policy，不读取页面 Attribute，也不推断 UseCase。无限制路由直接使用原生 RouteView。

UI 与服务端复用目录和判断语义，但身份状态可能不同；UI 可见性不构成安全边界，不能替代服务端检查。身份变化自动重查，过期异步授权结果不会覆盖较新的拒绝。不引入页面与 UseCase 对应表或角色表达式语言。

## 示例与迁移

SamplePermissions 提供共享目录；IPermissionDemoService.GetOverview 在 Contract 声明 Any 权限组，PermissionDemoService 的 Handler 保持普通实现。HTTP 映射 `/api/permissions/overview`，原生 JWT 集成测试覆盖 401、403 和两个允许分支。`/permissions-demo` 展示目录，并按权限标识直接使用 AuthGuard。现有 CRUD 访问规则不变；示例不会把 OAuth scope 自动兑换成业务权限，也不额外创造 UI 登录流程。

将裸/空 RequirePermission 改成 RequireAuthenticated；原 Handler 权限迁到对应 Contract 方法，并审查预期访问规则。移除 MetadataAttribute 及字符串键；自定义元数据用普通 Attribute 与专用消费者。SummaryAttribute 直接继承 Attribute，BCL Description/Category 仍可用。页面权限 Attribute 改成显式 UI 检查；PermissionRouteView 传入 Policy，或改用原生 RouteView。无需数据库结构变更。
