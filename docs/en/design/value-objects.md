# Value object construction

[简体中文](../../zh-CN/design/value-objects.md) · [Domain design](../../design/value-object.md)

Implement normalization and a boolean predicate; leave construction to NOF:

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

Normalize defaults to identity and IsValid defaults to true. The factory rejects null,
normalizes once, checks generated string length constraints, checks IsValid once, and
constructs an initialized value only on success. Direct calls to IsValid check only
that user predicate; use TryFrom to apply the complete construction rules.

From throws ArgumentException for rejected input and ArgumentNullException for a null
reference input. TryFrom returns false for expected invalid input and leaves its out
value at default; that sentinel must not be used as a valid domain value. Neither
factory catches exceptions from user hooks. A throwing Normalize/IsValid callback
propagates the same exception, including cancellation or technical failures. This
follows the [.NET Try pattern](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exceptions-and-performance).

For value-type primitives, From(T?) returns a nullable value object and preserves null;
TryFrom(T?, out ValueObject) returns false and default for null. New(IIdGenerator), JSON
reading, expression mapping, EF Core and NHibernate all construct through From, ensuring
the same rules. Initialization checks and AOT-friendly primitive JSON metadata remain.

Replace Of with From, and replace void Validate throwing for ordinary invalid input
with bool IsValid returning false. Expected application input failures should use
TryFrom and map false to a declared FailureCase. No Of or Validate compatibility layer
remains. NOF019 rejects a legacy Validate hook so migration cannot silently drop rules.
NOF013 rejects calls back into From/TryFrom from the same value object's hooks;
NOF014 warns when Normalize mixes in IsValid.

The configuration-node sample uses TryFrom directly instead of checking IsValid and
then constructing again. Its content predicate parses JSON once, with no duplicate
throwing validator. Error messages belong to the application operation's failure cases.

The only user validation signature is IsValid(T, out string? errorMessage). There is
no one-parameter validation hook. A null/blank failure message is allowed: the generated
factory supplies a useful fallback. The detailed TryFrom(value, out result, out message)
returns a non-null message on false and clears it to null on success; a convenience
overload discards the message. Null/normalization/length failures also produce messages.
From uses the same message in ArgumentException (ArgumentNullException for null input).
Both DomainException and DomainValidationException are deleted. Tracing no longer
converts coded domain exceptions to business rejection; use returned FailureCase values.
NOF019 rejects both legacy Validate and the old single-parameter IsValid hook.
