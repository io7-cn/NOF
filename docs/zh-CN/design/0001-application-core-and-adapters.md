# ADR 0001：应用执行核心与传输适配器

[English](../../en/design/0001-application-core-and-adapters.md) · [开发指南](../README.md)

- 状态：方向已接受，分阶段实施；每个实现仍须通过正常的独立代码评审。
- 日期：2026-11-11
- 负责人及验收负责人：XmmShp
- 评审人：每个合并候选须取得合格的领域及框架评审。
- 跟踪：[issue #4](https://github.com/io7-cn/NOF/issues/4)
- 前置依赖：[.NET 11 / Result 改造，PR #3](https://github.com/io7-cn/NOF/pull/3)
- 随阶段交付，取代[请求分发](../../design/request-dispatch.md)中“应用执行只以 RPC 为中心”的决定。

## 背景与约束

NOF 面向 HTTP、本地调用、后台任务和消息消费者，支持 DDD 与 Clean Architecture。
ASP.NET Core 提供 HTTP 能力，NOF 提供应用执行约定、领域边界，以及可靠持久化和消息语义。
DDD 不要求微服务部署，也不要求每个实体继承框架基类；简单 CRUD 可以保持简单。

此前的 RPC Handler 强制 IRpcResponse，入站上下文使用 IUnion 响应。Failure 已统一到
Abstraction，但 JSON 判别字段以及 Result 的传输工厂仍耦合了业务值和协议。
Domain 的 IRepository 暴露 LINQ、跟踪和 Attach 操作。把类型移进 Abstraction，
不等于它的接口就已传输无关。

迁移须保持租户隔离、授权、取消、事务/Outbox 一致性、生成的服务端调用和
Native AOT。已发布版本及标签不可变；依赖未合并变更时须记录前置依赖，不能绕过主分支评审门禁。

## 备选方案

1. 扩展为另一套 Web 框架，自建绑定、HTTP 结果、响应映射属性和 OpenAPI 模型。
   统一语法，但重复 ASP.NET Core 并让 HTTP 语义不断进入核心。
2. 保持 RPC 为唯一应用入口。延续现有约定，但本地用例和 REST 端点都要服从 RPC 返回约束。
3. 建立传输无关的应用执行核心，外部使用薄适配器。需要逐步拆分策略和元数据，
   但可复用宿主能力，让业务处理器脱离 Web 服务器执行。

## 决定

完全移除 Client 生成：保留 proto 风格契约及生成的服务端操作（包括静态 InvokeAsync），
不生成接口、HTTP 或 Local Client。HTTP 路由由原生端点模块显式声明，远程 HTTP 集成由
调用方维护，示例使用显式 UI 接口及适配器。已实现原生 IHttpEndpointModule、生成的操作描述符及 HTTP 策略桥接，配置 CRUD 已落地。
旧 HTTP RPC/JSON-RPC 适配器、HttpEndpoint 和 HTTP 路由生成器已删除；IRpcResponse 及生成的结果专用拒绝工厂已移除。

选择方案 3。用例接收类型明确的请求和执行上下文，返回声明的业务结果。
返回值可以是 DTO、可空值、Result<T> 或业务 Union，核心不要求 RPC 响应接口。
### 面向开发者的服务模型

保留服务契约、生成的操作基类以及既有的 `XxxService.DoSomething` 加 override 工作流。
UseCase 就是这套模型表达的应用操作，不是在 RPC 旁边再增加一套公开的 Handler 层级。
将以 RPC 为中心的服务、服务容器和处理器契约演进为传输无关的应用服务模型；
普通 DTO 和业务 Union 不应受 IRpcResponse 或必须声明传输方式的约束，RPC 成为适配器。

操作身份是“服务契约 + 操作”，不是请求/响应类型组合。不同操作可以共用输入和输出模型，
保留不同的行为和策略。继续由源码生成器通过应用程序集自动注册。
执行和策略机制可以隐藏在生成式调用之后；表达一个服务操作，不应要求开发者额外维护
独立 UseCase 类、RPC 转发类和另一套公开 Executor API。

原先并行的 IRequestHandler/IRequestExecutor 原型已经移除。实现沿用生成式服务操作，
使用 IApplicationService、ApplicationService<TService> 与 ServiceOperation<TRequest,TResponse>。
契约可以返回普通 DTO、可空值和业务 Union；本地调用无需传输特性，HTTP 暴露必须显式声明。
参见[迁移指南](application-requests.md)。

应用策略管线负责应用权限、验证、租户边界、事务策略和幂等。认证机制、HTTP 绑定及
状态/响应头、消息确认及重投递、线协议序列化属于适配器。领域不变量留在领域模型。
本地及非 HTTP 调用也必须执行需要的验证和应用授权。

执行拒绝独立于 Handler 的业务返回值。Handler 返回的业务 Failure 不等于认证或策略
拒绝；合法 null 业务结果不同于未初始化的执行结果。策略执行后才解析处理器，确保拒绝
请求不会构造或调用业务 Handler，也能在解析作用域依赖前完成上下文规范化。

随 Client 生成一同移除请求/响应出站管线。本地调用直接将 Context 和隔离的调用方身份
快照传入入站应用策略；远程 HTTP 行为使用原生 DelegatingHandler 和 .NET 跟踪能力。
Command/Notification 的消息出站投递管线继续保留。

### 职责划分

| 能力 | 方向 | NOF 的职责 |
| --- | --- | --- |
| HTTP 路由、绑定、内容协商、文件、Range 和条件请求 | 复用 ASP.NET Core | 调用应用用例的薄入口 |
| 状态码、Location、ETag 和 ProblemDetails | 保留在 HTTP 适配器 | 原生 TypedResults 和可选映射 |
| OpenAPI | 复用宿主元数据及 Schema 管线 | 提供准确的端点元数据 |
| DI、配置、日志和 Host 生命周期 | 复用 .NET | 注册约定及可选便利 Builder |
| OAuth/OIDC 及令牌验证 | 优先采用成熟协议实现 | 业务身份、权限及租户集成；单独评估替换 |
| 类型明确的应用执行及策略 | 保留并增强 | 跨入口一致的用例执行 |
| 领域规则、值对象及聚合边界 | 保留并增强 | 可选原语、分析器及隔离测试 |
| 事务、领域/集成事件、Outbox/Inbox、幂等 | 保留并增强 | 定义原子性及恢复行为，避免两个交付引擎竞争 |
| 通用 ORM 外观 | 收窄 | 面向聚合的写端口、应用查询端口；Provider 实现留在领域外 |
| UI、存储、缓存、映射及状态机 | 可选模块 | 以独立价值维护，不成为核心强制依赖 |

共享 Failure 保持为业务值。后续将判别字段、JSON 配置，以及 RPC 的空响应/失败构造
移到编解码器和适配器；成功 Message 属于业务 DTO。Success/Success<T> 已移除，无返回值 Result 使用 Empty。

执行上下文使用经过确认的业务身份和租户信息。适配器把原始头和 Claims 转换成该上下文。
复用 Activity/OpenTelemetry，不另建追踪运行时。环境访问只作可选便利，显式上下文是可测试契约。

持久化调整应收窄 Attach、AsNoTracking、SaveChanges(bool) 等 Provider 形状的接口。
聚合写入守住聚合边界，读取使用专门的查询服务。EF Core 负责自身事务机制，NOF 负责
业务状态与暂存集成消息的一致提交。不要只为引入另一套库就替换已有 Outbox；先比较语义、
恢复能力、许可证和迁移成本。

## 交付阶段与影响

1. **应用执行**：将生成式服务操作演进为传输无关的业务入口，保留策略与独立执行拒绝，
   通过同一个服务操作模型验证原生 ASP.NET Core 和本地调用，保持授权/租户检查有效。
2. **策略与上下文拆分**：共享应用规则迁入应用管线，协议规则留在适配器。
   保持每一步的安全行为，避免为去重而静默移除认证或授权。
3. **纯结果与编解码器**：IRpcResponse 已移除，继续分离核心业务结果上的 JSON 元数据，
   协议分类由编解码器和源生成元数据承担；协议变化时协调客户端与服务端。
4. **持久化边界**：建立面向聚合的写端口，分离读取模型，以 Provider 对等测试收窄通用 ORM 外观。
5. **可选集成**：简化宿主包装，按实际消费者需求和维护成本评估协议、UI、存储及状态机模块。

HTTP 响应 DSL 扩张不是主线。端点可直接用 TypedResults 映射已完成的业务值或执行拒绝；
文件、条件请求和流式响应细节留在 HTTP 适配器。

## 验证、部署和回滚

每阶段必须有可执行的纵向链路和有意义的回归测试。验证 DTO、Result/Union、可空返回、
取消、作用域生命周期、上下文传递、策略顺序、拒绝先于 Handler 构造、租户隔离及授权。
涉及服务端 HTTP 元数据、显式调用方适配器和 Native AOT 的路径时执行对应验证；领域测试独立于 Web、数据库和消息代理。

所有入口协调升级；旧 HTTP RPC 适配器已删除，不保留兼容层，由端点模块管理路由和协议。精确候选的证据记录在 issue/PR，
不把单次验收报告提交进源码。回滚恢复一致的包和宿主配置，不混用不兼容的契约或协议。

若适配器重复明显增加、入口间安全语义分歧，或实测 AOT/性能约束无法满足，则重新评估本决定。
新增抽象须有实际消费者和测试，避免为假设场景提前泛化。

## 参考

- [第一阶段实现指南](application-requests.md)

- [Clean Architecture 和应用边界](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [DDD 服务设计](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice)
- [ASP.NET Core 响应](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses)
- [EF Core 事务](https://learn.microsoft.com/ef/core/saving/transactions)

更新：通用 Contract Result/Result<T> 及消息头、字典便利扩展已删除。请使用当前[操作结果迁移指南](result-unions.md)。
