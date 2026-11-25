using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NOF.Infrastructure;

/// <summary>Shared value validation and union presentation; it is not an outcome wrapper.</summary>
public static class InvocationResult
{
    /// <summary>Rejects uninitialized native unions even when a caller allows null business values.</summary>
    public static void ValidateInitialization(object? value)
        => _ = Value(value);
    /// <summary>Reads the active native union branch for adapters and pattern matching.</summary>
    [return: NotNullIfNotNull(nameof(value))]
    public static object? Value(object? value)
    {
        while (value is IUnion union)
        {
            value = union.Value ?? throw new InvalidOperationException("The outcome response contains no case.");
        }
        return value;
    }
}
