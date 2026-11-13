# 宿主选择的调用扩展

[English](../../en/design/invocation-extensions.md) · [开发文档](../README.md)

Infrastructure 拥有 IInvocationFilter、InvocationDelegate、IInvocationPipeline、配置规则和有效执行计划。Application 拥有 InvocationContext、操作描述符与强类型业务 Handler。语义 Context 仍属于 Abstraction。本地事件处理器重命名为 LocalEventHandler<T>；IEventPublisher 和当前作用域事件语义保持不变。

业务 Handler 返回 Task<TBusiness>（包括生成的闭合 Result union）。Filter 和 next 返回 ValueTask<object?>，完整调用返回 Task<object?>。Filter 可以返回失败、以允许的业务值短路，或者查看并转换下游结果。ServiceExecution、ServiceCompleted、ServiceRejected、ServiceRejectedException 已删除。InvocationContext 不再保存可变响应或拒绝状态；克隆只保留调用身份与语义信息。技术异常和取消原样传播。

## 三层应用范围

```csharp
services.AddInvocationFilters(filters =>
{
    filters.Global.Use<AuditFilter>();
    filters.Service<IOrderService>().Use<RateLimitFilter>(10);
    filters.UseCase(OrderService.Read.Descriptor).Exclude<RateLimitFilter>();
});
services.UseInMemoryInvocationPipeline();
```

AddInvocationFilter<T>(order) 是 Global 的快捷方式。注册只声明扩展，由宿主决定如何执行。NOF 宿主默认选择内存引擎；自定义 IInvocationPipeline 同时实现 IInvocationPlanProvider.Describe，描述自己真实采用的执行计划。直接向 DI 注册 IInvocationFilter 不会启用阶段；先注册具体类型/工厂，再通过配置 API 应用。

默认嵌套 Global → Service → UseCase，Service 绑定 Contract 接口，UseCase 绑定完整描述符。每层按数字 order 及注册顺序排列。同层 Use/Exclude 以最后声明为准，后续显式 order 覆盖此前值；更深层优先，不受注册先后影响。重复启用只执行一次并保留外层位置，采用有效 order；先排除再在更深层启用则移动至该层。

已排除 Filter 不执行，也不调用 Describe、不贡献失败目录。配置不泄漏到同级服务、操作或其他宿主。首次解析计划后冻结，后续修改失败。生成的操作映射和实际实现类型元数据支持启动校验与描述，不需要构造处理器或 Filter。

Filter 在运行到该阶段时才从当前作用域解析，构造时已绑定规范化的语义上下文。引擎在各阶段和终端绑定/恢复 Context.Current，校验调用身份及返回值，并在解析依赖前检查取消。授权和租户选择先于 Handler 构造。内置 Global 顺序仍为 tracing(-400)、instrumentation(-300)、可选 authentication(-200)、tenant(-100)、authorization(-50)、host tenant(-25)。

## Filter 自有的失败声明

```csharp
[FailureCase("Exceeded", "000429")]
[FailureCase("Unavailable", "Limit service unavailable", typeof(LimitDetails))]
public sealed partial class RateLimitFilter : IInvocationFilter
{
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object input,
        InvocationDelegate next, CancellationToken ct)
    {
        if (input is ReadOrderRequest { Page: < 0 })
        {
            return new Exceeded("Invalid page");
        }
        return await next(context, input, ct);
    }
}
```

FailureCaseAttribute 继续定义在 Contract，也支持标在顶层非泛型 partial Filter 类上。Infrastructure.SourceGenerator 生成嵌套 sealed 分支及静态只读 RateLimitFilter.FailureCases 目录；不生成 Filter.Result 或工厂。全部分支通过 IFailure 提供 Title/Detail，自定义分支额外实现 IFailure<TError> 并提供 Error。Name 是 C# 标识符，Title 是非空摘要，不作状态判别。Contract.SourceGenerator 仍只做分析，非法声明报告 NOF220。

IInvocationFilter.FailureCases 静态虚成员默认空，Describe 默认 IncludeAll。自定义静态描述可以选择本 Filter 的子集：

```csharp
public static void Describe(InvocationDescriptionContext description)
{
    if (description.Origin == InvocationOrigin.Http)
    {
        description.Include<Exceeded>();
    }
}
```

Include<TFailure>() 不能选择本目录以外的类型。描述只提供操作、来源、配置/Attribute 元数据，不提供作用域服务或请求身份。内置授权描述与执行共用策略解析，包含真实实现侧元数据、宿主租户选项和自定义授权器配置。

有效 InvocationPlan 同时包含实际 Filter、选中的 FailureCases 与操作 BusinessCases。返回不属于有效目录的失败、未知业务值和未初始化 union 都是契约错误。适配器可通过 InvocationResult.Value(value) 解开装箱 union 再用原生 switch；执行路径保留原始业务返回值。

原生 HTTP 功能扩展使用泛型操作映射、声明式 Request/Response 和不可变绑定驱动的运行时 OpenAPI。按确切类型的整条响应映射消费有效失败目录并保留原生 challenge。参见 [HTTP 适配](http-adaptation.md)。消息消费使用相同结果路径，预期 Filter 失败回滚业务事务并将 inbox 标为失败，不执行技术异常重试。消息直接发送与 outbox 均不恢复出站中间件。

## 破坏性迁移

- InMemoryEventHandler<T> 改名为 LocalEventHandler<T>，保留 IEventPublisher。
- Filter/管道与配置引用 NOF.Infrastructure；返回 next 的值或已声明分支，不再修改 InvocationContext。
- 完整调用按 object 值匹配，业务 Handler 保持强类型闭合返回；删除执行包装、Reject/SetResponse 和预期失败异常处理。
- 直接 IInvocationFilter DI 应用改为 Global/Service/UseCase 的 Use/Exclude。
- 自定义引擎提供真实有效计划；运行时、生成器、适配器和调用方配套升级。

[统一操作](unified-operations.md) · [可预期失败](expected-failures.md) · [分层边界](contract-boundaries.md)
