using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NOF.Domain;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>
/// Typed <see cref="ValueConverter{TModel,TProvider}"/> for a value object.
/// </summary>
public sealed class ValueObjectConverter<TValueObject, TPrimitive> : ValueConverter<TValueObject, TPrimitive>
{
    public ValueObjectConverter(MethodInfo fromMethod, MethodInfo castMethod)
        : base(
            vo => (TPrimitive)castMethod.Invoke(null, new object?[] { vo })!,
            primitive => (TValueObject)fromMethod.Invoke(null, new object?[] { primitive })!)
    {
    }
}
