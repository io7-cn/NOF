# 基于表达式的映射

[English](../../en/design/mapper.md)

NOF 使用同一份注册表达式完成数据库查询投影与内存映射。`MappingRegistration` 以封闭的源类型、目标类型和可选名称组成 `MapKey`，并保存表达式及由 `Of<TSource, TDestination>()` 创建的泛型查询适配器。`MappingRegistry` 在 ApplicationPart 初始化期间收集注册，创建 `IMapper` 时冻结。

`ExpressionMapper.GetExpression` 返回已展开表达式供 `IQueryable.Select` 使用；`Map` 编译并缓存同一表达式，启用解释执行以适配提前编译（AOT）。`Mapper.Current` 绑定到当前异步执行流。`ProjectTo<TDestination>()` 不要求 Application 引用 EF Core。

## 生成与手写注册

```csharp
[Mappable<Order, OrderDto>]
[Mappable<Order, OrderSummary>]
public static partial class Mappings;
```

每个方向都需要明确声明。生成器通过程序集初始化器注册表达式，不退回运行时 `IMapper.Map`。支持大小写不敏感的属性/构造参数匹配、直接与内置数字转换、可空值、值对象包装/解包、集合 Select/ToList/ToArray，以及已声明的嵌套映射。不自动生成 Parse、Enum.Parse 或任意业务方法等依赖查询 Provider 的转换。

需要定制时，直接注册可审查的表达式：

```csharp
services.AddMapping<Order, OrderDto>(order => new OrderDto
{
    Id = (long)order.Id,
    Name = (string)order.Name
});
```

精确匹配映射名称，不支持运行时对象类型映射、开放泛型回退或只提供委托的替代路径。非泛型查询入口只从 `IQueryable.ElementType` 恢复源类型，仍调用相同的泛型表达式适配器。

## 查询与嵌套映射

先过滤、排序和分页，再调用 ProjectTo：

```csharp
var items = await db.Orders
    .Where(order => order.IsActive)
    .OrderBy(order => (long)order.Id)
    .Take(20)
    .ProjectTo<OrderDto>()
    .ToListAsync(cancellationToken);
```

嵌套映射直接替换参数，不生成 `Expression.Invoke`；只解析明确注册的名称，检查缺失注册、依赖环与捕获服务常量。`ExpressionMapper` 创建时会提前验证映射。值对象到原始类型使用显式转换，反向调用 From；不同值对象之间的转换需要手写业务映射。

## 诊断

| 标识 | 含义 |
| --- | --- |
| NOF020 | 重复映射声明 |
| NOF021 | Mappable 目标必须是 partial static |
| NOF022 | Optional/可空语义不兼容 |
| NOF023 | 缺少所需嵌套映射声明 |
| NOF024 | 目标构造参数或 required 成员无法绑定 |
| NOF025 | ProjectTo 之后继续进行服务端查询整形 |
| NOF026 | Queryable.Select 中调用 IMapper.Map 而不是查询投影 |

查询翻译使用关系数据库集成测试验证，不能用 EF 内存 Provider 代替。参见[值对象](value-object.md)、[持久化边界](persistence-boundaries.md)和[生成器隔离](source-generators.md)。
