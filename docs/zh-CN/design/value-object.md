# 值对象为何不提供 Value 属性

[English](../../en/design/value-object.md)

值对象使用实现 `IValueObject<T>` 的 `readonly partial struct`。开发者提供 `IsValid(value, out errorMessage)` 与可选 Normalize，生成器提供 From、TryFrom、相等比较、显式原始类型转换、文本和 JSON 支持。Normalize 只负责规范化，不应递归调用 From、TryFrom 或 IsValid。完整构造约定见[工厂与迁移指南](value-objects.md)。

## 未初始化值

C# 允许结构体被零初始化，default、数组元素与泛型 default(T) 都能绕过构造器。生成的独立初始化标记仅在规范化与校验成功后设置，因此合法的 Score.From(0) 不等于 default(Score)。两个未初始化值彼此相等，供 ORM（对象关系映射器）使用零初始化哨兵；但转换、散列、文本、JSON 和持久化转换都会拒绝把它当成有效值，抛出 InvalidOperationException。公开的无参构造也会抛出，NOF018 在编译时拒绝显式 default/new()。

## 字符串长度只声明一次

```csharp
[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct OrderName : IValueObject<string>
{
    public static bool IsValid(string value, out string? errorMessage)
    {
        var valid = !string.IsNullOrWhiteSpace(value);
        errorMessage = valid ? null : "Invalid value.";
        return valid;
    }
}
```

Normalize 之后、用户 IsValid 之前进行长度检查；MinimumLength 默认 0。长度按 string.Length 计算，EF Core/NHibernate 使用 MaximumLength 配置列长度，不把最小长度当作数据库列属性。NOF016/017 检查类型和范围；NOF306 提醒重复配置，NOF307 拒绝冲突，NOF308 提醒基础设施的固定长度尚未声明到领域类型。动态值在构建模型时仍会检查。相关代码修复可以移除重复配置或迁移缺失声明，Fix All 会合并同一值对象的相同长度；冲突值保留供人工处理。字节限制、索引长度和列类型仍属于 Provider。

## 显式转换与查询

NOF 不提供 Value 属性；业务代码应传递值对象，仅在协议、日志或存储边界明确解包：

```csharp
string raw = (string)orderName;
var order = await db.Orders.FirstOrDefaultAsync(
    item => item.Name == orderName, cancellationToken);
```

原生 ValueConverter 处理相等查询，调用者不需要访问对象内部成员。具体查询仍受数据库 Provider 的翻译能力约束，不能推断任意成员访问均可翻译。生成器只定义相等语义，不擅自定义业务排序：

```csharp
var orders = query.OrderBy(order => (long)order.Id);
```

NOF015 检查值对象排序键；类型明确实现 IComparable 或 IComparable<TSelf> 时，表示业务已自行定义排序，诊断不再报告。

## 与映射的关系

原始类型提取只支持精确底层类型，通过 `(T)value` 完成；包装通过 `VoType.From(value)` 完成。不自动把字符串值对象转换成数字，也不根据名称推断两个不同值对象之间的业务转换。跨值对象转换需要明确表达式。日志可使用 ToString，业务协作应尽可能保留强类型。

参见[表达式映射](mapper.md)、[值对象工厂](value-objects.md)和[原生 EF 配置](efcore-configuration.md)。
