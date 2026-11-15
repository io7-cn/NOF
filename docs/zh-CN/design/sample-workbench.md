# 交互式 Sample 能力工作台

[English](../../en/design/sample-workbench.md) · [RabbitMQ 路由](rabbitmq-sample.md) · [鉴权](authorization.md)

Sample 演示业务框架能力，不提供身份服务器。AppHost 启动生产者、Notifications 接收服务、Keycloak、RabbitMQ、PostgreSQL 和 Garnet。页面调用真实端点，展示持久化发件状态与接收端业务投影，不发送真实邮件。

## 本地启动

启动 Docker Desktop，安装仓库固定的 .NET SDK 与工作负载，在仓库根目录执行：

```powershell
dotnet build sample/NOF.Sample.AppHost -c Release
dotnet run --project sample/NOF.Sample.AppHost -c Release --no-build --launch-profile http -p:AspireUseCliBundle=false
```

本机所有 dotnet 命令需要提权。HTTP Profile 仅用于本地开发。打开 AppHost 输出的 Dashboard 登录地址，其中访问令牌每次启动不同。等待资源健康后，打开[能力工作台](http://localhost:55892/demo.html)。

若使用 Aspire CLI，需要确认它启动的是当前构建配置，避免构建 Release 后运行陈旧的 Debug AppHost。CLI 可能创建本地 aspire.config.json，它不是必须提交的项目源码；上面的原生启动命令会关闭 CLI 跳转。

| 服务 | 本地地址或参数 |
|---|---|
| 能力工作台 | http://localhost:55892/demo.html |
| 配置 CRUD | http://localhost:55892/ |
| 流式响应 | http://localhost:55892/streaming-demo |
| 权限组件 | http://localhost:55892/permissions-demo |
| Notifications | http://localhost:55893/api/messages/receipts |
| Keycloak | http://localhost:18080，Realm 为 `nof` |
| Keycloak 管理 | `nof-admin` / `nof-admin-local` |
| RabbitMQ 管理 | http://localhost:15672，`guest` / `guest` |
| Aspire Dashboard（HTTP Profile） | http://localhost:18888，使用启动输出中的登录令牌 |

Keycloak 开发容器启动时导入 `sample/NOF.Sample.AppHost/Realms/nof-realm.json`。浏览器客户端为 `nof-sample-web`，回调地址 `http://localhost:55892/signin-oidc`，API Audience 为 `nof-sample`。认证使用原生 ASP.NET Core Cookie/OpenIdConnect/JwtBearer。仓库中的客户端密钥和账号仅用于本地演示，不能用作部署配置。改变生产者端口时，也要修改 Realm 的回调、登出与 Origin 地址。

## 鉴权场景

在工作台点击“登录 Keycloak”和“退出登录 / 切换账号”。切换用户需要完整退出提供者会话；再次打开登录可能会复用当前 SSO 身份。

| 身份 | 密码 | 读取 | 管理 |
|---|---|---|---|
| 匿名 | — | 401 | 401 |
| alice | `AliceDemo!2026` | 200 | 200 |
| bob | `BobDemo!2026` | 200 | 403 |
| charlie | `CharlieDemo!2026` | 403 | 403 |

这些是实际 Contract 权限检查，页面隐藏不能代替执行授权。Keycloak 的 `entitlements` Claim 提供权限授予。权限页独立演示 AuthGuard，不读取 UseCase 声明。服务端组件将原生已认证身份复制到作用域 UI 身份；业务调用转到线程池，避免示例的同步领域事件发布在等待异步数据库处理时阻塞 Blazor 同步上下文。

## 可以亲手操作的能力

| 入口 | 操作与观察 |
|---|---|
| 配置中心 | 创建根节点和子节点，添加 JSON 文件、激活并查看预览，删除测试数据。重复名称、无效输入、删除存在子节点的父节点展示声明式失败。该流程还使用值对象、生成的 DTO 映射、原生 EF 聚合/查询端口、领域事件与工作单元。 |
| Outbox 与消息 | 暂停发件，使用 `demo@example.test` 提交订单，观察两条已持久化意图；恢复后等待三条接收服务业务投影。展开完整响应可以查看上下文与 Headers。 |
| 重试 | 点击“让下一次投递失败一次”，再提交订单。一条意图会记录失败，之后变为已发送，尝试次数增加；上次失败信息仍保留用于解释历史。 |
| 业务幂等 | 重复投递当前订单。虽然消息 ID 不同，同一订单仍只有三条投影，由接收端租户范围内的业务唯一约束保证。 |
| 租户缓存 | host 写入，tenant-a 读取不存在，再切回 host 读取存在；逻辑键相同，Garnet 提供 Redis 缓存。 |
| 对象存储 | 保存、读取、删除固定的本地示例对象。 |
| 值对象与 ID | 分别验证空名称和合法名称，查看用户自定义错误信息，生成五个 Snowflake ID。 |
| 本地事件 | 发布文本，查看自动发现的 LocalEventHandler 记录。 |
| 执行 Filter | 刷新真实操作的耗时、来源、租户与 Trace；Dashboard 可查看两个服务和原生客户端的链路与日志。 |
| 流式响应 | 开始 SSE，观察增量事件，取消调用并触发预期失败。 |
| OpenAPI | 查看原生 HTTP 端点描述；Contract 不包含传输路由。 |

每个订单保存两条传输无关意图：SendEmail 发到一个队列，OrderCreated 发到有两个队列绑定的 Topic Exchange。生产者基础设施中的 OutboxHandler 选择路由，调用集成包的具体 RabbitMQMessageClient。接收端将队列直接映射到操作；CountOrder 无须暴露为 Contract 方法。框架不包含 Inbox，ACK 在执行完成之后发送，提交与幂等性由业务负责。邮件投影只是模拟副作用，不是真实 SMTP。

暂停、单次失败和 `/api/demo` 诊断端点仅在 Development 开启。暂停只影响本进程的示例 OutboxHandler，不会暂停 RabbitMQ，也不会撤销已开始的网络发送。测试结束后恢复发件。Filter 和事件观测有数量上限，只保存在内存；Outbox 与业务投影持久化保存。这些管理型演示控制不构成生产环境授权设计。

## 既有开发数据库

MigrateOnStart 会应用本地生产者的 EF 迁移。RemoveInbox 会拒绝仍有未解决旧任务的数据库；ExternalIdentityProvider 仅移出活动身份模型，保留物理旧表。升级部署环境前，应备份并审查每个租户数据库。

Notifications 的 EnsureCreated 只初始化新 SQLite 文件。旧 Receipts 表缺少 IdempotencyKey 时，先停止接收端并备份，再审查 `Persistence/UpgradeBusinessIdempotency.sql`。使用遇错即停的客户端执行一次，例如 `sqlite3 -bail notifications.db ".read Persistence/UpgradeBusinessIdempotency.sql"`，然后重启接收端。脚本推导订单键与旧行键，不删除历史；未解决的订单/类型/租户重复记录会使唯一索引创建失败，客户端退出时回滚，需要显式审查重复记录。不得删除已使用的数据库来替代升级。

## 测试隔离

可选 Broker 测试通过 NOF_SAMPLE_RABBITMQ 指定连接。请使用独立 RabbitMQ Virtual Host，不能直接使用正在运行的演示默认 Virtual Host，否则测试队列会与演示消费者竞争。原生回调、Schema 升级、权限和投影幂等性测试也覆盖无外部 Broker 的路径。浏览器测试验证实际本地服务和身份集成，不代表生产身份提供者协议合规认证。
