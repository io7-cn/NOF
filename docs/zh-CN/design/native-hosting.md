# 原生宿主集成

[English](../../en/design/native-hosting.md) · [开发指南](../README.md)

NOF 通过扩展原生 Builder 接入，不再提供另一套宿主。直接使用 WebApplicationBuilder、
HostApplicationBuilder、WebAssemblyHostBuilder 或 MauiAppBuilder。
Build 只同步组装应用，启动、停止由平台生命周期负责；注册不会启动后台任务或访问数据库。

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddNOF();
builder.AddApplicationPart(typeof(NodeService).Assembly);
builder.AddApplicationService<NodeService>();

var app = builder.Build();
app.UseNOF();
app.MapNodeEndpoints();
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
```

Web Builder 的 AddNOF 注册默认服务端基础设施及 HTTP 适配器；Worker Builder 的 AddNOF
注册服务端基础设施。重复注册幂等。ApplicationPart 显式声明，不再从调用栈推断程序集。
builder.AddApplicationPart 与 services.AddApplicationPart 在同一容器中共享去重记录。
加入操作实现及生成注册所在程序集；普通 HTTP 映射扩展不需要 ApplicationPart 注册。

UseNOF 为每个原生 HTTP 请求成对绑定、恢复作用域便捷服务；普通 DI scope 无激活协议，参见 [Ambient 绑定](ambient-binding.md)。显式调用普通 MapFeatureEndpoints 扩展、原生 MapHealthChecks 和 MapOidcServer，不再发现或注册 DI 端点模块。AddNOF 注册 ProblemDetails、JSON 和 OpenAPI，MapOpenApi 由宿主选择。认证、异常处理、防伪和协议仍由原生宿主负责。参见 [HTTP 适配](http-adaptation.md)。

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddNOF();
builder.AddApplicationPart(typeof(InvoiceHandler).Assembly);
using var host = builder.Build();
await host.RunAsync();
```

Worker 引用 NOF.Infrastructure 即可，不再需要 NOF.Hosting.Console。
NOF.Hosting 保留为轻量集成包，不实现另一套宿主。

## 启动与停止

MigrateOnStart 注册一个原生 IHostedLifecycleService，在 StartingAsync 中完成全部宿主数据库迁移，
之后才允许任何 IHostedService.StartAsync，包括 HTTP 和消息消费者。迁移失败或取消会中止启动；
只调用 Build 不迁移数据库。租户库迁移继续在 ApplicationStarted 后后台执行。
迁移维持作用域、租户上下文恢复和取消传播。

OIDC 客户端预置在 IHostedService.StartAsync 中执行，位于迁移阶段之后。
需要预置先于后续服务及 HTTP 完成时，使用原生默认的顺序启动；主动启用并发 StartAsync 会失去此顺序保证。
两种配置下，数据库迁移仍先于所有 StartAsync。NOF 不静默覆盖 HostOptions，也不新增初始化排序引擎。
自定义启动任务使用原生 Hosted Service 及明确的依赖关系。

## 部署身份

DeploymentOptions 替代挂在 IHostEnvironment 上的扩展状态，通过 services.Configure<DeploymentOptions>
配置。保留 ServiceName、ServiceId、InstanceId 配置键，ServiceName 默认使用原生 ApplicationName。
PrimaryNode 可以显式指定维护任务主节点，否则实例一为主节点。
Snowflake ID、消息路由、顺序键及诊断标签读取同一份宿主 Options；不替换原生环境注册及其生命周期。

## 客户端与测试

WebAssemblyHostBuilder.CreateDefault(args).AddNOF() 和 MauiApp.CreateBuilder().AddNOF()
只注册客户端 UI 能力。使用平台 Build 和生命周期；不创建并行 IHost，不手动遍历 IHostedService，
也不启动服务端 Inbox/Outbox。客户端需要生成注册时使用 services.AddApplicationPart。

测试使用 Host.CreateApplicationBuilder().AddNOFTest() 和 BuildTestHost()，保留应用测试辅助工具。
BuildTestHost 不启动宿主。需要迁移、预置或后台任务时，显式 await host.Host.StartAsync()。
HTTP 集成测试使用原生 ASP.NET Core 测试服务器。

## 破坏性迁移

删除 NOFWebApplicationBuilder、NOFConsoleHostBuilder、NOFWebAssemblyHostBuilder、NOFMauiAppBuilder、
NOFWebAssemblyHost、NOFMauiApp 及 NOFTestAppBuilder 用法，改用原生 Builder 与注册扩展。
删除 BuildNOFAsync、InitializeNOFAsync、IApplicationInitializationStep 及初始化 Compare/拓扑 API、
DependencyGraph、NOF401 分析器/修复和 NOF.Hosting.SourceGenerator。
MigrateOnInitialize 改为 MigrateOnStart，数据库断言从构建阶段迁至启动阶段。
环境扩展属性及主节点 Predicate 改为 DeploymentOptions。显式注册端点程序集并映射模块。
所有包和宿主协调升级；回滚时恢复配套旧包与配置。

可靠消息由 AddOutbox/AddMessageBindings 按需装配，参见[能力与 Worker 配置](transactional-outbox.md#按能力装配与独立-worker-宿主)。
