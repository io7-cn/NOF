# NOF 开发说明

[English](../en/README.md) · [共享开发规范](../../.standards/docs/zh-CN/README.md)

NOF 由仓库维护者负责，当前维护者及验收责任人为 XmmShp。维护者在交付前指定
备份联系人和相关领域审查人。生效规范版本由 `.standards` Git 子模块记录的精确
提交确定，验证和发布期间不得跟随上游移动分支。

使用 `global.json` 固定的稳定版 .NET SDK 和工作负载集。运行时项目面向 .NET 11，
编译器生成器保留 `netstandard2.0`。格式、还原、构建和测试命令见
[框架开发规则](../../.agents/rules/nof-dev.md)。克隆时使用 `--recurse-submodules`，
或运行 `git submodule update --init --recursive`；记录接入提交后运行
`python .standards/.ci/check-adoption.py`。

工作开始前创建关联工作项，使用 `feature/2-result-unions` 这样的全小写主题分支。
刷新 `main`，保留已有主题工作，通过拉取请求（供独立审查的拟议变更）合入 `main`。
普通变更压缩为符合 Conventional Commits（约定式提交格式）的提交。破坏性更新
须在变更请求和最终提交正文说明兼容影响及迁移方法。

必过检查包含规范与贡献检查、Windows/Linux 构建及测试。Linux 另外执行格式检查
和 Native AOT（编译为原生可执行文件）验证。检查通过不等于获准合并。维护者须
授权精确候选版本，并取得 [REV-02](../../.standards/docs/zh-CN/01-governance/review-standards.md)
要求的独立审批；破坏性 API 和授权更新需两个独立合格审核主体，其中包括领域
所有者。托管功能无法直接强制执行时，合并前须设置等效的受保护分支门禁。

现有 `main` 交付工作流在验证后发布唯一版本的 nightly NuGet 包。稳定版由明确的
版本标签或已记录的手动工作流触发。既有已发布版本（包括 `v11.0.0`）不可变，
后续受保护发布标签必须具有可验证签名。主题分支验证不创建标签或稳定版。

另见[贡献说明](../../CONTRIBUTING.md)、[架构](../design/index.md)、
[安全报告](../../SECURITY.md)和 [Apache 2.0 许可证](../../LICENSE)。
