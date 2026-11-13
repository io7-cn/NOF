# 值对象构造

[English](../../en/design/value-objects.md) · [领域设计](../../design/value-object.md)

用户只提供规范化和布尔校验，由 NOF 生成构造逻辑：

```csharp
[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct CustomerName : IValueObject<string>
{
    public static string Normalize(string value) => value.Trim();
    public static bool IsValid(string value, out string? errorMessage)
    {
        var valid = !string.IsNullOrWhiteSpace(value);
        errorMessage = valid ? null : "Invalid value.";
        return valid;
    }
}

var trusted = CustomerName.From(" Alice ");
if (!CustomerName.TryFrom(input, out var name, out var errorMessage))
{
    return new InvalidInput(errorMessage);
}
```

Normalize 默认原样返回，IsValid 默认返回 true。工厂拒绝 null，规范化一次，检查生成的
字符串长度规则，再调用一次 IsValid；只在全部通过后构造已初始化的值。直接调用 IsValid
只运行用户谓词；需要完整构造规则时应使用 TryFrom。

From 对非法值抛 ArgumentException，对引用类型的 null 输入抛 ArgumentNullException。
TryFrom 对普通非法输入返回 false，out 参数重置为 default，不能将此哨兵作为有效领域值。
两个工厂都不捕获用户回调异常：Normalize 或 IsValid 抛出的技术异常、取消异常等原样传播。
这遵循 [.NET Try 模式](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exceptions-and-performance)。

底层为值类型时，From(T?) 返回可空值对象并保留 null；TryFrom(T?, out 值对象) 对 null
返回 false 和 default。New(IIdGenerator)、JSON 读取、表达式映射、EF Core 和 NHibernate
统一通过 From 构造，应用同一组规则。初始化检查和 AOT 友好的基础类型 JSON 元数据保持。

将 Of 改为 From，将对普通非法输入抛异常的 void Validate 改为返回 false 的 bool IsValid。
可预期的应用输入失败使用 TryFrom，并把 false 映射到声明的 FailureCase。没有 Of 或
Validate 兼容层。NOF019 拒绝遗留 Validate 钩子，避免迁移时静默丢失校验规则。
NOF013 禁止在同一值对象的钩子中回调 From/TryFrom；NOF014 提醒 Normalize 不应混入 IsValid。

配置节点示例直接使用 TryFrom，避免先 IsValid 再构造导致重复校验。配置内容的 JSON
只解析一次，不再维护第二套抛异常的 Validate；业务错误消息由应用操作的失败分支提供。

用户校验统一为 IsValid(T, out string? errorMessage)，不再提供单参数校验钩子。
失败时允许省略消息内容（返回 null 或空白），生成的工厂会提供默认文案。
TryFrom(value, out result, out message) 失败时给出非空消息，成功时清空为 null；
便利重载可以忽略消息。null、规范化结果为 null、长度不符也会给出对应消息。
From 使用相同消息构造 ArgumentException（null 输入为 ArgumentNullException）。
DomainException 和 DomainValidationException 均已删除；Tracing 不再将带码领域异常
自动转为业务拒绝，业务失败应返回 FailureCase。NOF019 同时拒绝旧 Validate 和单参数 IsValid。
