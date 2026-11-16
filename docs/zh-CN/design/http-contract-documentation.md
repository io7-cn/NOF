# HTTP 契约文档

[English](../../en/design/http-contract-documentation.md)

OpenAPI 描述公开的 HTTP 契约及已知输入、业务错误。它不能承诺穷尽基础设施故障、取消时机、断线或发送响应头后的流错误，也不能仅凭业务 Union 推导全部 HTTP 行为。

## 责任归属

| 内容 | 负责人 |
| --- | --- |
| 投影请求的字段存在性、可空性、绑定来源和默认值 | NOF HTTP 适配器 |
| 内置 JSON/参数绑定的 400、JSON 媒体类型的 415 | 实际产生这些错误的绑定器 |
| 有效业务/Filter 分支及最终整条响应映射 | NOF HTTP 适配器 |
| Contract Summary/Description 与默认唯一操作标识 | NOF HTTP 适配器；原生端点元数据可覆盖 |
| 原生认证方案、Cookie、防伪与重定向 | 应用宿主 |
| 公开业务错误数据、稳定 Problem type 与 Location | 应用端点模块 |
| 领域输入约束、动作取值与应用 SSE 事件语义 | 应用端点模块 |
| 普通 Minimal API 的响应类型和不透明委托 | 原生 TypedResults/Produces 元数据与应用 Transformer |

原生 WithName 保留其操作标识；否则适配器按操作身份、HTTP 方法和路由生成唯一标识，同一操作映射多个端点也不会重复。

适配器区分“字段必须出现”和“值允许 null”。拆分请求属性时，不可变输入描述保留 JsonPropertyInfo.IsSetNullable；生成 Schema 时包装引用，不修改共享组件。完整构造的正文保留原生 Schema。内置正文读取器声明 400/415；不透明 Custom 读取器应显式声明自身实际行为，不能因为声明了自定义纯文本正文就虚构 JSON 415。

有效失败分支把分支名称或 Filter 目录标识以及声明标题写入响应说明。多个分支共用状态、媒体类型和正文时，共用一个 Schema，并保留全部已知分支说明。这是文档信息，不是按文本判断的运行时分类器，也不自动成为机器错误码。默认失败仍由 Title/Detail 生成 ProblemDetails，框架不自动暴露任意自定义 Error。被排除的 Filter 同时从执行与文档移除；不执行响应工厂来猜测行为。

## Sample 的原生 HTTP 决策

开发工作台使用具名响应 DTO 和显式 Produces。非法动作、缺少订单 ID 返回 400 ProblemDetails；值对象拒绝有自己的强类型 400 正文，防伪失败使用 application/problem+json。原生 JSON 输入错误由宿主的 ProblemDetails 状态码页面呈现。收件投影查询采用明确网关契约：有效下游数据转换成 MessageReceiptsResponse；下游失败、非法 JSON 或连接失败转换为 502 ProblemDetails，请求取消继续传播。

删除带子节点的节点时，Sample 显式返回 NodeHasChildrenProblem，error 属性保留 NodeHasChildrenError，type 为 urn:nof:sample:config-node:has-children。完整原生映射及配套 Produces 属于 Sample，框架通用失败格式保持不变。创建节点的响应同时声明实际的 Location 响应头。

权限操作声明 Cookie 会话或 JWT Bearer 两种认证方式，并说明真实的权限分组。登录、登出是经过外部身份提供者的浏览器重定向流程。工作台 POST 和登出同时需要 X-CSRF-TOKEN 与 nof.sample.antiforgery：先 GET /api/demo/security-token，保留响应 Cookie，再发送返回的 token。会话凭据与防伪凭据承担不同职责。

SSE 使用 OpenAPI 3.2 itemSchema 描述单个事件对象，其 data 字符串包含 JSON SampleStreamEvent，而不是把整条连接描述成一个 JSON 文档。应用负责说明租户 Header、started/tick/completed 消息、默认主题、count 收敛到 1–20、间隔收敛到 100–5000ms 的行为。这些范围表示截取，不表示越界拒绝；标准 SSE 帧仍由 ASP.NET Core 负责。

## 验证

契约测试同时生成文档并发送真实 TestServer 请求，覆盖 null、非法 JSON、错误媒体类型、结构化失败、Location、防伪拒绝/接受、原生登录重定向和下游 502。Schema 测试还检查失败说明、排除 Filter、SSE 事件结构和安全要求。已知 HTTP 分支改变时增加回归测试，不把未验证的技术故障宣称为穷尽。

参见 [HTTP 适配](http-adaptation.md)、[配置 CRUD](configuration-rest.md)和[能力工作台](sample-workbench.md)。原生元数据遵循 [ASP.NET Core OpenAPI](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/include-metadata)，流格式遵循 [OpenAPI 3.2 SSE](https://spec.openapis.org/oas/v3.2.0.html#special-considerations-for-server-sent-events)。

业务专属的原生操作 Transformer 直接附在所属端点上，不通过全局 Transformer 比较路由字符串来选取。

## 业务选择与 Schema 边界

允许在文件尚不存在时提前指定活跃文件名。设置合法名称会保存选择；配置合并在文件内容添加前忽略该节点的缺失文件。fileName 为 null、缺省或空字符串时清除选择。HTTP 说明遵循已有行为，不额外引入文件必须存在的业务规则。

Outbox 视图使用真实的 OutboxMessageStatus 枚举，并只在此属性上配置字符串序列化；当前为 Pending、Sent、Failed。保留已有 JSON 字符串，不全局改变枚举格式。Headers 是字符串键到字符串或 null 的对象。回执 kind 继续使用数字：0 = Mail、1 = OrderAudit、2 = OrderMetrics；Sample 宿主从 Contract 枚举生成其文档。消费者应显示未知的未来状态/种类，而不是把它解释成某个已知状态。Contract 显式固定数值，保持持久化与传输身份。

名称验证继续使用 .NET string.Length，即 UTF-16 单元数，并拒绝纯空白。JSON Schema minLength/maxLength 按 Unicode 码点计数，因此 1–100 是必要边界，不能表达所有应用约束。50 个补充平面 emoji 占 100 个 UTF-16 单元，可以接受；60 个占 120 个，会被拒绝，尽管码点数未达到 100。说明明确这个额外规则，不改变领域行为，也不引入自定义正则方言来伪装两种计数等价。参见 [JSON Schema 字符串长度](https://json-schema.org/draft/2020-12/json-schema-validation#name-maxlength)。

可读的失败说明不等于机器可判别的失败标识。统一展示可以继续使用通用 ProblemDetails；需要机器决策的端点显式选定稳定 Problem URI 和结构化数据，如删除冲突。不要求新增全局 CLR 类型到 URI 的目录，也不要求每个失败都增加一份 DTO。

原生 SSE 端点通过既有 HTTP 默认映射，从有效调用计划生成 Filter 失败说明。默认匿名策略不声明不可达的 401/403/422；宿主启用严格租户规则时再纳入实际的 401/403。声明的流失败与原生查询绑定仍贡献 400。文档跟随宿主策略，不硬编码状态全集。
