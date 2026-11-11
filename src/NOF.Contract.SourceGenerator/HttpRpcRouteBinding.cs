using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NOF.Contract.SourceGenerator;

/// <summary>Shares the binding rules for parameterized Controller RPC routes.</summary>
public sealed class HttpRpcRouteBinding
{
    public List<(string Name, IPropertySymbol Property, IMethodSymbol? StaticParser)> Path { get; } = new();
    public List<IPropertySymbol> Remaining { get; } = new();
    public bool HasBody { get; private set; }

    public static HttpRpcRouteBinding? Create(
        IMethodSymbol method, INamedTypeSymbol requestType, HttpVerb verb, string route, out string error)
    {
        error = string.Empty;
        if (verb is not (HttpVerb.Get or HttpVerb.Post or HttpVerb.Put or HttpVerb.Delete or HttpVerb.Patch))
        {
            error = "the HTTP verb is not supported";
            return null;
        }

        if (method.ContainingType.ContainingType is not null
            || method.ContainingType.IsGenericType
            || method.ContainingType.IsFileLocal
            || method.IsGenericMethod
            || method.GetAttributes().Count(static attr => attr.AttributeClass?.ToDisplayString() == ApplicationServiceHelpers.HttpEndpointAttributeFqn) != 1)
        {
            error = "use one HTTP endpoint on a non-generic method in a top-level, non-generic contract";
            return null;
        }

        if (!ApplicationServiceHelpers.TryGetServiceReturnInfo(method, out var result) || result.IsStream)
        {
            error = "parameterized routes require a unary RPC result";
            return null;
        }

        if (requestType.TypeKind != TypeKind.Class
            || requestType.IsAbstract
            || requestType.IsFileLocal
            || !requestType.InstanceConstructors.Any(static ctor => ctor.Parameters.Length == 0
                && ctor.DeclaredAccessibility == Accessibility.Public))
        {
            error = "the request must be a concrete class with a public parameterless constructor";
            return null;
        }

        var binding = new HttpRpcRouteBinding();
        var properties = ApplicationServiceHelpers.GetAllPublicProperties(requestType);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in route.Split('/'))
        {
            if (!ApplicationServiceHelpers.ContainsRouteParameters(segment))
            {
                continue;
            }

            if (segment.Length < 3 || segment[0] != '{' || segment[segment.Length - 1] != '}')
            {
                error = $"'{segment}' must be a complete path segment of the form '{{name}}'";
                return null;
            }

            var name = segment.Substring(1, segment.Length - 2);
            if ((!char.IsLetter(name[0]) && name[0] != '_')
                || name.Any(static ch => !char.IsLetterOrDigit(ch) && ch != '_')
                || !names.Add(name))
            {
                error = $"'{segment}' must contain a unique, required parameter name";
                return null;
            }

            var matches = properties.Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || !CanBind(matches[0]))
            {
                error = $"'{{{name}}}' must match one publicly readable and writable request property";
                return null;
            }

            var property = matches[0];
            var type = property.Type;
            var hasScalarParser = CanParseScalar(type);
            var staticParser = hasScalarParser ? null : FindStaticPathParser(type);
            if (type is not INamedTypeSymbol { IsRefLikeType: false }
                || (!hasScalarParser && staticParser is null))
            {
                error = $"path property '{property.Name}' must be a string, enum, or a type that implements IParsable<TSelf> or declares a compatible public static TryParse";
                return null;
            }

            binding.Path.Add((name, property, staticParser));
        }

        var bodyMethod = verb is HttpVerb.Post or HttpVerb.Put or HttpVerb.Patch;
        foreach (var property in properties)
        {
            if (names.Contains(property.Name))
            {
                continue;
            }

            if (bodyMethod && GetJsonIgnoreCondition(property) == 1 && !property.IsRequired)
            {
                continue;
            }

            if (!CanBind(property))
            {
                error = $"property '{property.Name}' must have a public getter and setter or init accessor";
                return null;
            }

            if (!bodyMethod && !CanParseQuery(property.Type))
            {
                error = $"query property '{property.Name}' must be a string, enum, or implement IParsable<T>";
                return null;
            }

            if (bodyMethod && GetJsonIgnoreCondition(property) == 1)
            {
                error = $"required body property '{property.Name}' cannot be ignored";
                return null;
            }

            if (bodyMethod && property.GetAttributes().Any(static attr => IsUnsupportedJsonAttribute(attr.AttributeClass)))
            {
                error = $"body property '{property.Name}' uses an unsupported property-level JSON attribute";
                return null;
            }

            binding.Remaining.Add(property);
        }

        binding.HasBody = bodyMethod && binding.Remaining.Count > 0;
        return binding;
    }

    public static ITypeSymbol GetQueryValueType(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    public static bool IsOptionalQuery(IPropertySymbol property)
        => !property.IsRequired && (property.Type.NullableAnnotation == NullableAnnotation.Annotated
            || property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });

    public static AttributeData? FindJsonAttribute(IPropertySymbol property, string name)
        => property.GetAttributes().FirstOrDefault(attr =>
            attr.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization." + name);

    public static int? GetJsonIgnoreCondition(IPropertySymbol property)
    {
        var attribute = FindJsonAttribute(property, "JsonIgnoreAttribute");
        return attribute is null
            ? null
            : attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "Condition").Value.Value as int? ?? 1;
    }

    private static bool CanBind(IPropertySymbol property)
        => property.GetMethod?.DeclaredAccessibility == Accessibility.Public
            && property.SetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static bool CanParseQuery(ITypeSymbol type)
        => CanParseScalar(GetQueryValueType(type));

    private static bool CanParseScalar(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_String
            || type.TypeKind == TypeKind.Enum
            || type is INamedTypeSymbol { TypeKind: not TypeKind.Interface } namedType
                && namedType.AllInterfaces.Any(candidate =>
                    candidate.OriginalDefinition.ToDisplayString() == ApplicationServiceHelpers.ParsableFqn
                    && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], namedType));

    private static IMethodSymbol? FindStaticPathParser(ITypeSymbol type)
    {
        return type.GetMembers("TryParse").OfType<IMethodSymbol>()
            .Where(method => method is
            {
                MethodKind: MethodKind.Ordinary,
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                IsAbstract: false,
                IsVirtual: false,
                IsGenericMethod: false,
                IsVararg: false,
                RefKind: RefKind.None,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                Parameters.Length: 2 or 3
            }
                && method.Parameters[0] is { RefKind: RefKind.None, Type.SpecialType: SpecialType.System_String }
                && (method.Parameters.Length == 2 || (method.Parameters[1].RefKind == RefKind.None
                    && method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IFormatProvider"))
                && method.Parameters[method.Parameters.Length - 1].RefKind == RefKind.Out
                && SymbolEqualityComparer.Default.Equals(method.Parameters[method.Parameters.Length - 1].Type, type))
            .OrderByDescending(static method => method.Parameters.Length)
            .FirstOrDefault();
    }

    private static bool IsUnsupportedJsonAttribute(INamedTypeSymbol? attributeType)
    {
        for (var type = attributeType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString() is "System.Text.Json.Serialization.JsonConverterAttribute"
                or "System.Text.Json.Serialization.JsonExtensionDataAttribute"
                or "System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute")
            {
                return true;
            }
        }

        return false;
    }
}
