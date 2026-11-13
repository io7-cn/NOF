# NOF.Domain

Domain primitives package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

`NOF.Domain` currently provides the low-level domain building blocks that are shared across the framework:

- `IValueObject<T>` for source-generated value objects
- `[NewableValueObject]` for `long`-backed ID value objects
- `[ValueObjectLength]` for string-backed value-object length constraints shared with persistence providers
- Domain-owned business values for predictable rule outcomes
- Standard .NET argument exceptions for invalid construction
- `IIdGenerator` and the ambient `IdGenerator` facade
- `IRepository<TEntity>`, `IAsyncQueryable<T>`, and provider-agnostic batch update descriptors for repository abstractions
- `AddNOFDomain()` for package-local runtime registration

Unit-of-work orchestration remains in `NOF.Application` through `IDbContext`; repository abstractions live here so Domain services can express collection dependencies without referencing Application.

## Key Abstractions

### `IValueObject<T>`

Implement `IValueObject<T>` on a `readonly partial struct` to define a value object. The source generator produces:

- a private constructor accepting the primitive value
- a throwing `From(T)` factory and a non-throwing `TryFrom(T, out value)` sharing Normalize/IsValid
- an explicit cast operator to the primitive type
- equality members and `ToString()`
- a nested `JsonConverter`

Always construct a value object through `From(...)` or another generated factory. Diagnostic `NOF018` rejects direct `default` and parameterless `new()` expressions. Because the CLR can still zero-initialize structs implicitly (for example in arrays, fields, and generic code), generated value objects also track validated initialization at runtime. An uninitialized sentinel never compares equal to a validated value and throws `InvalidOperationException` if it is converted, hashed, formatted, serialized, or passed through the persistence converter. Two uninitialized values compare equal to support ORM sentinel checks. A valid primitive default such as `Score.From(0)` remains valid.

User code only implements static bool IsValid(T, out string? errorMessage), plus optional Normalize(T). Validate is
removed; NOF019 reports its old hook signature. From throws ArgumentException when
TryFrom rejects the value (ArgumentNullException for null reference input). TryFrom
returns false for null, length violations or IsValid=false and clears its out value.
Exceptions from Normalize/IsValid propagate unchanged. Nullable primitive From(null)
preserves absence; nullable TryFrom(null) returns false. Of is removed without aliases.

The generated `JsonConverter` is AOT-friendly and resolves primitive serialization through `JsonTypeInfo`.
If you use Native AOT-style publishing, make sure the `JsonSerializerOptions` passed to serialization includes metadata for the primitive type backing the value object.
Best practice: keep `Normalize(T)` limited to canonicalization such as trimming or casing, and avoid calling `From(...)` or `IsValid(...)` from inside `Normalize`.
When using a value object as a LINQ ordering key, cast it to its primitive type, for example `orders.OrderBy(order => (long)order.Id)`. Diagnostic `NOF015` warns when an `OrderBy` family operation relies on the default comparer for a value object. Implement `IComparable<TSelf>` or `IComparable` explicitly when the value object itself has intentional domain ordering semantics.

```csharp
using NOF.Domain;

public readonly partial struct CustomerName : IValueObject<string>
{
    public static bool IsValid(string value, out string? errorMessage)
    {
        var valid = !string.IsNullOrWhiteSpace(value);
        errorMessage = valid ? null : "Invalid value.";
        return valid;
    }
}
```

For a string-backed value object, declare its length range once on the value-object type:

```csharp
[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct CustomerName : IValueObject<string>
{
    public static bool IsValid(string value, out string? errorMessage)
    {
        var valid = !string.IsNullOrWhiteSpace(value);
        errorMessage = valid ? null : "Invalid value.";
        return valid;
    }
}
```

The source generator validates the normalized string against the minimum and maximum before calling the custom `IsValid` predicate. `MinimumLength` defaults to zero; supported persistence providers use only `MaximumLength` for the column facet. Explicit constant `HasMaxLength` calls produce `NOF306` when redundant, `NOF307` when conflicting, and `NOF308` when the value object has no domain declaration; dynamically computed values are checked when the persistence model is built.
Diagnostic `NOF016` rejects the attribute on anything other than `IValueObject<string>`, and `NOF017` rejects invalid ranges.

### `[NewableValueObject]`

Apply `[NewableValueObject]` to a `readonly partial struct` implementing `IValueObject<long>` to generate:

- `New(IIdGenerator generator)` for explicit ID generation
- `New()` as a convenience wrapper over the ambient `IdGenerator.Current`

```csharp
using NOF.Domain;

[NewableValueObject]
public readonly partial struct OrderId : IValueObject<long>;
```

Before using `New()`, register NOF.Domain and ensure the current scope activates daemon services:

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddNOFDomain();
```

`New()` is a convenience API over the ambient `IdGenerator`. Use `New(IIdGenerator)` when you want an explicit dependency instead of relying on ambient scope.
Standard NOF hosts activate daemon services automatically; custom hosts should do the equivalent when entering a scope.

`AddNOFDomain()` registers the default `SnowflakeIdGenerator`.
If you want a different generator, register your own `IIdGenerator` in the container and let your host/container policy decide how it overrides the default:

```csharp
services.AddNOFDomain();
services.AddSingleton<IIdGenerator, MyIdGenerator>();
```

### Expected failures and invariant exceptions

Declare expected application failures in Contract using FailureCase. Domain methods
return their own business values; Application maps them to the generated operation
cases. Domain does not reference Contract. See the [expected-failure guide](../../docs/en/design/expected-failures.md).

The old Domain FailureAttribute, static-holder generator and failure.Throw extension
are removed. DomainException and DomainValidationException are also removed; use
standard .NET exceptions for programmer misuse and returned cases for business rejection.

## Installation

```shell
dotnet add package NOF.Domain
```

## License

Apache-2.0

Declared expected application outcomes belong to Contract. Shared Abstraction.Failure
remains available to policy rejection and is not propagated through custom domain exceptions.

IsValid always takes out string? errorMessage; single-parameter hooks report NOF019.
TryFrom(value, out result, out errorMessage) preserves user validation text and supplies
messages for built-in constraints. Its message is non-null on false and null on success.
From uses that same message in ArgumentException. The shorter TryFrom overload discards it.
