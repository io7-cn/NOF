# 代码生成器隔离

[English](../../en/design/source-generators.md) · [HTTP 适配](http-adaptation.md)

生成器只读取用户声明和稳定的引用元数据，可以独立根据原始声明推导名称并输出对生成类型的引用，不能读取同一次编译中其他生成器的产物。

Abstraction 生成注册/本地事件注册；Domain 生成值对象/只读集合；Application 生成操作基类、闭合 Result/失败分支、静态描述符/访问器及注册/映射。Contract.SourceGenerator 保留声明分析器，不生成 Contract 代码；Infrastructure 生成 Filter 失败分支/目录；EF Core 生成提供者映射支持。

Hosting.AspNetCore.SourceGenerator 只为 MapGet/MapPost/MapPut/MapPatch/MapDelete/MapOperation<Operation> 生成具体 Request 配置扩展。从已引用操作基类读取类型，或独立解析用户 ApplicationService<TContract> 与方法，支持继承契约。不生成路由/Client，不从函数体推断 HTTP Schema，不执行用户委托；运行时 OpenAPI 描述最终不可变端点绑定。

组合回归以正反两种顺序运行全部生成器，覆盖单程序集中的泛型 HTTP 操作映射及具体请求 lambda。独立元数据 Contract/原生 union 测试和关闭反射的 HTTP 冒烟覆盖跨程序集边界。无关操作类型被泛型约束在编译期拒绝。NuGet 内嵌分析器，ProjectReference 消费端显式添加生成器分析器引用。

NOF.CodeFixes 随各层分析器提供确定性的局部修复，语义设计选择只报告诊断；union 穷尽检查使用原生 C#。不保留检查结果分析器、旧 HTTP 路由/Client 生成器、初始化生成器及 NOF401。
