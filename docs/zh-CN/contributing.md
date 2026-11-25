# 参与 NOF 开发

[English](../en/contributing.md)

维护责任人：NOF 维护者（XmmShp）。阅读固定版本的[已采用标准](../../.standards/docs/en/README.md)与[项目开发说明](README.md)。协作时保持尊重、具体和建设性，使用最小示例报告可复现行为，不包含私有数据。

## 环境与工作流

克隆时包含子模块，使用 global.json 并安装适用的 MAUI/Android 工作负载。本仓指定的 Windows 开发机器执行 dotnet 命令时需要提权。前端格式工具使用 Node 22 与锁定的 Prettier，安装命令为 npm ci --ignore-scripts。

检查工作区，刷新 main，创建或复用小写主题分支，例如 refactor/4-code-layout，保留无关改动。禁止直接在受保护分支编辑、提交或推送。用普通引用关联 Issue，合并即可满足验收条件时可用 GitHub 支持的关闭指令。使用 Conventional Commit 标题并说明破坏性变化与迁移。推送主题分支后向 main 提交拉取请求（PR）。大范围纯格式整理应与行为变化分开审查。

## 代码与文档

使用共享 EditorConfig/Prettier 基线，类默认 public，使用文件作用域命名空间、Allman 控制块与显式取消，公开库 API 补充 XML 文档。保留依赖方向与公开名称，遵循[文件组织](development/code-organization.md)。生成器独立读取声明和引用元数据，不读取其他生成器的输出。

同时更新中英文文档并提供互链。包 README 作为工具可发现的简短入口，完整用法放入 docs/en/packages 与 docs/zh-CN/packages。不把已删除 API 翻译成当前推荐用法；决策文档保留历史背景并链接当前设计。截图、单轮报告与临时脚本放入忽略的 artifacts 或外部审查证据，不纳入版本控制。

## 验证

格式检查需要已构建的生成器程序集，与测试使用同一 Release 输出。必需检查失败时先修复，再重跑受影响检查。

```sh
dotnet restore NOF.slnx -p:Configuration=Release
dotnet build NOF.slnx -c Release --no-restore
dotnet format NOF.slnx --no-restore --verify-no-changes
dotnet test NOF.slnx -c Release --no-build --no-restore
npm ci --ignore-scripts
npm run check:frontend
python .ci/check-documentation.py
python .standards/.ci/check-adoption.py
python .ci/check-contribution.py
```

格式检查设置 Configuration=Release。模型变化需要相关原生持久化测试与 EF 待迁移模型检查；持续集成还会在 Linux 验证 Native AOT。可选 RabbitMQ 测试使用独立 Virtual Host，不能竞争正在演示的队列。结果保留在 PR/持续集成产物中，不提交单轮验收报告。

## 审查与交付

使用批准的贡献者身份与 Conventional Commit squash 消息。合并前通过必需的 Windows/Linux 持续集成与规范检查，并解决阻塞讨论。建议进行独立本地审查，高风险变更征求相关领域所有者的意见，重大更新后进行复查。在 PR 中记录针对当前候选版本的审查结论和验证证据，由维护者据此授权合并。Nightly 和正式发布使用授权工作流，保留已发布版本。审查者应能通过 PR 了解问题、行为、迁移和验证结果。
