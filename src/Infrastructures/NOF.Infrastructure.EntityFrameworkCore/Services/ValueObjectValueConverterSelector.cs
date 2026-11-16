using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NOF.Domain;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>
/// An EF Core <see cref="IValueConverterSelector"/> that automatically provides
/// <see cref="ValueConverter"/> instances for any type implementing
/// <c>IValueObject&lt;TPrimitive&gt;</c> from NOF.Domain.
/// <para>
/// The value object must expose:
/// <list type="bullet">
///   <item>A static <c>From(TPrimitive)</c> factory method.</item>
///   <item>An explicit cast operator to <c>TPrimitive</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ValueObjectValueConverterSelector : ValueConverterSelector
{
    private static readonly ConcurrentDictionary<Type, ValueConverterInfo?> Cache = new();
    private static readonly Type InterfaceOpenType = typeof(IValueObject<>);
    private static readonly string FromMethodName = "From";

    public ValueObjectValueConverterSelector(ValueConverterSelectorDependencies dependencies)
        : base(dependencies) { }

    public override IEnumerable<ValueConverterInfo> Select(Type modelClrType, Type? providerClrType = null)
    {
        var baseConverters = base.Select(modelClrType, providerClrType);

        var underlyingType = Nullable.GetUnderlyingType(modelClrType) ?? modelClrType;

        var info = Cache.GetOrAdd(underlyingType, BuildConverterInfo);
        if (info is null
            || (providerClrType is not null
                && providerClrType != info.Value.ProviderClrType))
        {
            return baseConverters;
        }

        return baseConverters.Prepend(info.Value);
    }

    [RequiresDynamicCode("Calls System.Type.MakeGenericType(params Type[])")]
    private static ValueConverterInfo? BuildConverterInfo([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type voType)
    {
        var primitiveType = GetPrimitiveType(voType);
        if (primitiveType is null)
        {
            return null;
        }

        var fromMethod = voType.GetMethod(FromMethodName,
            BindingFlags.Public | BindingFlags.Static,
            null, [primitiveType], null);
        if (fromMethod is null)
        {
            return null;
        }

        var castMethod = voType.GetMethod("op_Explicit",
            BindingFlags.Public | BindingFlags.Static,
            null, [voType], null);
        if (castMethod is null || castMethod.ReturnType != primitiveType)
        {
            return null;
        }

        var converterType = typeof(ValueObjectConverter<,>).MakeGenericType(voType, primitiveType);
        var converterInstance = (ValueConverter)Activator.CreateInstance(converterType, fromMethod, castMethod)!;

        return new ValueConverterInfo(
            modelClrType: voType,
            providerClrType: primitiveType,
            factory: _ => converterInstance);
    }

    private static Type? GetPrimitiveType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type voType)
        => voType.GetInterfaces()
            .Where(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == InterfaceOpenType)
            .Select(iface => iface.GenericTypeArguments[0])
            .FirstOrDefault();
}
