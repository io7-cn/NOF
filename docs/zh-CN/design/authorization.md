# 契约授权与动态权限

[English](../../en/design/authorization.md) · [宿主装配](host-composition.md)

NOF 负责权限声明、认证要求和 All/Any 检查语义，原生认证建立可信 ClaimsPrincipal。Abstraction 提供不可变策略、默认检查器与授予来源；Contract 保留声明特性，Application 负责 AuthorizationPolicyResolver。UI 直接使用 Abstraction，不读取 Contract 或 UseCase。权限目录及其存储由应用负责。

## 声明操作要求

```csharp
using NOF.Abstraction;
using NOF.Contract;

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

RequirePermission 要求非空白标识并隐含认证。默认 All，不同声明组之间使用 AND。方法声明替换接口默认值；只有实际派生声明覆盖祖先，不同继承分支存在歧义时必须明确声明。AllowAnonymous 取消本契约的继承默认要求，不绕过宿主原生认证或租户约束。没有声明的契约允许匿名。

权限仅标在 Contract 接口和操作方法上，Handler、请求/消息类型及 UI 页面都不是声明来源。NOF240 检查非法组，NOF241 拒绝匿名与认证声明矛盾，NOF242 拒绝实现侧目标。自定义 Attribute 可实现 IPermissionMetadata，不再使用通用字符串键值协议。

## 动态权限数据

AddNOF 与 AddNOFUI 通过已有 Abstraction 注册入口自动安装默认 Scoped 检查服务。不再提供 AddPermissions、框架 PermissionDefinition 或冻结的 PermissionCatalog；启动与端点描述前无需注册权限标识。框架检查声明形式，不验证标识是否属于业务字典。

```csharp
services.AddScoped<IPermissionGrantProvider, DatabasePermissionGrantProvider>();

var granted = await permissions.IsGrantedAsync("config.read", ct);
```

业务实现接收 ClaimsPrincipal、权限标识与显式语义 Context，查询数据库、外部服务或业务维护的缓存。不存在、已禁用或未授予的权限应返回 false。名称、分组、目录管理、角色数据及缓存失效都属于业务；NOF 不增加目录 Provider 门面或持久化结构。

契约策略快照可以缓存，但 NOF 不缓存授权决策，每次调用都会执行检查。新增或撤销授予可在同一作用域的下次检查中生效，无需重启、重建 DI 或刷新执行计划。Provider 自己缓存时负责其新鲜度。自定义来源可在默认装配前后注册，多次默认装配不会覆盖它。

默认 ClaimsPermissionGrantProvider 仅检查已认证 Identity 中的精确/通配权限 Claim，不查询目录、不将 OAuth scope 或角色转换为权限，也不查询动态存储。Claim 更新取决于宿主刷新身份；需要服务端即时撤权时，应选用数据库授予实现。

## 执行与上下文

IPermissionChecker.IsGrantedAsync 使用 Scoped IUserContext 与 Context.Current。CheckAsync 接收显式不可变 AuthorizationPolicy、主体和 Context，返回 Granted、AuthenticationRequired 或 Denied。不同组之间为 AND，组内使用各自 All/Any。查询异常与取消直接传播，不会变成允许。

HTTP、本地调用和消息消费共享缓存后的契约要求，在构造业务 Handler 前检查。租户信任校验先于授予来源的构造与查询；Provider 在显式已认证租户上下文中延迟解析，确保 Scoped 持久化使用可信租户，并恢复外层 ambient 状态。匿名拒绝不构造授予服务。消息不恢复生产者 Claims/Token，宿主建立可信消费者身份；自定义执行引擎负责落实有效计划。

生成的操作初始化器为裁剪保留继承契约方法；独立的裁剪调用方使用 Resolver 时也需保留所选继承声明。

## UI 与示例

```razor
<AuthGuard Permission="config.write">
    <ChildContent><button>编辑</button></ChildContent>
    <UnauthorizedContent><span>只读</span></UnauthorizedContent>
</AuthGuard>
```

AuthGuard 检查权限标识或 UI 自己的显式 Policy，不能同时传入；两者都不传时要求认证。PermissionRouteView 接收必填 Policy，不读取页面 Attribute 或 UseCase 契约。身份变化触发重查，过期异步授权不能覆盖新拒绝。外部授予数据变化需业务触发 UI 刷新或失效，没有自动数据库订阅。UI 展示控制不能替代服务端校验。

SamplePermissions 只持有示例自己的 SamplePermission 展示数据。IPermissionDemoService 声明 Any 权限组，普通 Handler 返回示例数据，`/permissions-demo` 直接按标识使用 AuthGuard。JWT 集成测试覆盖 401/403 及两个允许分支，不注册任何权限目录。示例列表不是执行白名单，也没有增加角色或组织管理。

## 迁移

删除 AddPermissions 调用及框架目录/定义模型依赖。需要目录时，将 DTO 和查询放入业务自己的契约与服务；动态授予实现 IPermissionGrantProvider。检查模型使用 NOF.Abstraction，契约解析使用 NOF.Application。权限只声明在 Contract，纯登录要求用 RequireAuthenticated。旧页面特性改成显式 UI 检查，已删除的 HasPermission 改用 IPermissionChecker。本轮不要求数据库结构迁移。
