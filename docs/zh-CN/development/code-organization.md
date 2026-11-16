# 代码与文档组织

[English](../../en/development/code-organization.md)

遵循固定版本的[共享风格](../../../.standards/docs/en/02-development/coding-style.md)。本页明确 NOF 的具体目录安排，不改变公开命名空间或持久化负载标识。

## 源代码

核心项目按职责划分：Abstractions 放端口，Models 放复用数据，Services 放实现，Extensions 放装配，Authorization/Invocation/Outbox 等能力目录保留内聚子系统。Provider 与 Hosting 项目集中原生适配职责。Domain 负责不变量，Contract 声明不依赖 Application、Domain 或 Infrastructure。

默认每个文件一个顶层类型；同名泛型/非泛型家族可以保留在一起。操作请求与直接响应放在 `<Operation>Request.cs`，复用模型与枚举独立放入 Models。服务接口放在 Contract 根目录。仅供测试的夹具可以与所属行为测试共存，以方便理解；这些明确分组不是把无关公开类型聚集在大文件中的理由。

拆分的 partial 类使用用途后缀。生成成员与原生迁移区别于手写源码；使用匹配固定 EF 运行时的工具重新生成迁移，并审查模型、Schema 和数据影响，不机械重排迁移 Designer 或生成代码。生成器中的源码片段属于数据，不能按纯文本重命名。

静态字段和常量用 PascalCase，私有实例字段用 _camelCase，局部函数、变量与参数用 camelCase；异步接口允许时使用 Async 后缀。保留外部名称与序列化名称。方法保持单一内聚操作，在边界处理预期失败，传播技术错误与取消。

## 前端

根 .editorconfig 与 .prettierrc.json 对齐共享基线。C#/Razor 四空格，JavaScript/CSS/JSON/YAML/项目 XML 两空格。package.json/package-lock.json 固定格式工具；运行 npm ci --ignore-scripts 与 npm run check:frontend。排除第三方 wwwroot/lib 和生成输出。全局样式采用功能前缀，Blazor 组件样式放在组件旁。

## 文档

完整维护正文放在 docs/en 与 docs/zh-CN 的对应路径并互链。包/根 README、CONTRIBUTING、SECURITY 保留为工具可发现的简短入口；旧 docs/design 的有效内容迁入规范目录后删除原文件，直接修正引用和导航，不保留跳转壳。生成的 API 参考沿用公开 XML 注释，双语指南解释边界，不翻译代码标识。

同时更新两个版本及导航。文档检查验证语言对齐、互链与维护中的本地链接，DocFX 验证生成参考与站点。运维型指南注明责任人与审查日期，单轮证据放在忽略的 artifacts 或外部 Issue/PR/持续集成记录中。
