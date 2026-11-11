using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Contract.SourceGenerator;
/// <summary>Analyzes RPC service declarations for source generation.</summary>

public static class ApplicationServiceHelpers
{
    public const string HttpEndpointAttributeFqn = "NOF.Contract.HttpEndpointAttribute";
    public const string ParsableFqn = "System.IParsable<TSelf>";
    public const string TransportOverAttributeFqn = "NOF.Contract.TransportOverAttribute";
    public const string TransportOverHttpAttributeFqn = "NOF.Contract.TransportOverHttpAttribute";
    public const string TransportOverMemoryAttributeFqn = "NOF.Contract.TransportOverMemoryAttribute";
    public const string SummaryAttributeFqn = "NOF.Contract.SummaryAttribute";
    public const string ApplicationServiceInterfaceFqn = "NOF.Contract.IApplicationService";
    public const string ResultInterfaceFqn = "NOF.Contract.IRpcResponse<TSelf>";
    public const string StreamingResultFqn = "System.Collections.Generic.IAsyncEnumerable<T>";
    public const string ResultFqn = "NOF.Contract.Result";
    public const string GenericResultFqn = "NOF.Contract.Result<T>";
    public const string EmptyFqn = "NOF.Contract.Empty";

    public static bool HasHttpEndpointAttribute(IMethodSymbol symbol)
    {
        return symbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == HttpEndpointAttributeFqn);
    }

    public static bool IsApplicationServiceInterface(INamedTypeSymbol symbol)
        => symbol.TypeKind == TypeKind.Interface
           && (symbol.ToDisplayString() == ApplicationServiceInterfaceFqn
               || symbol.AllInterfaces.Any(i => i.ToDisplayString() == ApplicationServiceInterfaceFqn));

    public static bool IsApplicationServiceContract(INamedTypeSymbol symbol)
        => IsApplicationServiceInterface(symbol)
           && symbol.ToDisplayString() != ApplicationServiceInterfaceFqn;

    public static List<IPropertySymbol> GetAllPublicProperties(INamedTypeSymbol typeSymbol)
    {
        var properties = new List<IPropertySymbol>();
        var seen = new HashSet<string>();
        var current = typeSymbol;
        while (current != null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false, GetMethod: not null } prop
                    && seen.Add(prop.Name))
                {
                    properties.Add(prop);
                }
            }

            current = current.BaseType;
        }

        return properties;
    }

    public static bool ContainsRouteParameters(string route)
    {
        return route.IndexOf('{') >= 0 || route.IndexOf('}') >= 0;
    }

    public static bool TryGetHttpTransport(INamedTypeSymbol serviceInterface, out HttpTransportInfo transport)
    {
        var attribute = serviceInterface.GetAttributes()
            .FirstOrDefault(static attr => attr.AttributeClass?.ToDisplayString() == TransportOverHttpAttributeFqn);
        if (attribute is null
            || attribute.ConstructorArguments.Length == 0
            || attribute.ConstructorArguments[0].Value is not int style)
        {
            transport = default;
            return false;
        }

        var routePrefix = attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Value as string
            : null;
        transport = new HttpTransportInfo((HttpRpcStyle)style, routePrefix);
        return true;
    }

    public static bool HasMemoryTransport(INamedTypeSymbol serviceInterface)
        => serviceInterface.GetAttributes()
            .Any(static attr => attr.AttributeClass?.ToDisplayString() == TransportOverMemoryAttributeFqn);

    public static bool IsCancellationToken(ITypeSymbol typeSymbol)
        => typeSymbol.ToDisplayString() == "System.Threading.CancellationToken";

    public static bool TryGetRequestParameter(IMethodSymbol method, out IParameterSymbol? parameter)
    {
        if (method.Parameters.Length == 1 && !IsCancellationToken(method.Parameters[0].Type))
        {
            parameter = method.Parameters[0];
            return true;
        }

        parameter = null;
        return false;
    }

    public static bool TryGetServiceReturnInfo(IMethodSymbol method, out ServiceReturnInfo returnInfo)
    {
        if (method.ReturnsVoid)
        {
            returnInfo = default;
            return false;
        }

        var returnType = method.ReturnType;
        if (returnType.ToDisplayString() is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.ValueTask")
        {
            returnInfo = default;
            return false;
        }

        if (returnType is INamedTypeSymbol { IsGenericType: true } namedType
            && (namedType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<T>"
                || namedType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask<T>"))
        {
            returnInfo = default;
            return false;
        }

        returnInfo = new ServiceReturnInfo(returnType);
        return true;
    }

    public static string GetFullNamespace(INamespaceSymbol ns)
    {
        var parts = new List<string>();
        while (ns is not null && !string.IsNullOrEmpty(ns.Name))
        {
            parts.Insert(0, ns.Name);
            ns = ns.ContainingNamespace;
        }

        return string.Join(".", parts);
    }

    public static EndpointInfo ExtractEndpointInfo(ServiceMethodInfo method)
    {
        var attributes = method.Method.GetAttributes();
        var methodAttr = attributes
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == HttpEndpointAttributeFqn);

        var httpMethod = methodAttr is null
            ? HttpVerb.Post
            : (HttpVerb)methodAttr.ConstructorArguments[0].Value!;

        var route = methodAttr is null
            ? method.OperationName
            : methodAttr.ConstructorArguments.Length > 1
                ? methodAttr.ConstructorArguments[1].Value as string
                : null;

        route = route?.TrimEnd('/');
        route ??= method.OperationName;

        var displayName = TryGetCtorString(attributes, "NOF.Contract.EndpointNameAttribute");

        var description = TryGetCtorString(attributes, "System.ComponentModel.DescriptionAttribute");

        var summary = TryGetCtorString(attributes, SummaryAttributeFqn);

        var tags = attributes
            .Where(a => a.AttributeClass?.ToDisplayString() == "System.ComponentModel.CategoryAttribute")
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
            .Where(v => v != null)
            .Cast<string>()
            .ToArray();

        return new EndpointInfo
        {
            RequestType = method.RequestType,
            ReturnInfo = method.ReturnInfo,
            Method = httpMethod,
            Route = route,
            OperationName = method.OperationName,
            DisplayName = displayName,
            Description = description,
            Summary = summary,
            Tags = tags
        };
    }

    private static string? TryGetCtorString(ImmutableArray<AttributeData> attributes, string attributeFqn)
        => attributes
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeFqn)
            ?.ConstructorArguments.FirstOrDefault().Value as string;


}
/// <summary>Describes an RPC service method for source generation.</summary>

public sealed class ServiceMethodInfo
{
    public IMethodSymbol Method { get; set; } = null!;
    public INamedTypeSymbol? RequestType { get; set; }
    public ServiceReturnInfo ReturnInfo { get; set; } = new(null!);
    public string OperationName { get; set; } = string.Empty;
}
/// <summary>Describes an HTTP endpoint for RPC source generation.</summary>

public class EndpointInfo
{
    public INamedTypeSymbol? RequestType { get; set; }
    public ServiceReturnInfo ReturnInfo { get; set; } = new(null!);
    public HttpVerb Method { get; set; }
    public string Route { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Summary { get; set; }
    public string[] Tags { get; set; } = [];
}
/// <summary>Describes an RPC return type for source generation.</summary>

public readonly struct ServiceReturnInfo
{
    public ServiceReturnInfo(ITypeSymbol valueType)
    {
        ValueType = valueType;
    }

    public ITypeSymbol ValueType { get; }

    public bool IsEmpty
        => ValueType.ToDisplayString() == ApplicationServiceHelpers.EmptyFqn;

    public bool IsNonGenericResult
        => ValueType.ToDisplayString() == ApplicationServiceHelpers.ResultFqn;

    public bool IsGenericResult
        => ValueType is INamedTypeSymbol { IsGenericType: true } namedType
           && namedType.OriginalDefinition.ToDisplayString() == ApplicationServiceHelpers.GenericResultFqn;

    public ITypeSymbol? PayloadType => IsGenericResult ? ((INamedTypeSymbol)ValueType).TypeArguments[0] : null;

    public bool HasSuccessWrapper => PayloadType is INamedTypeSymbol { IsGenericType: true } payload
        && payload.OriginalDefinition.ToDisplayString() == "NOF.Contract.Success<T>";

    private ITypeSymbol? StreamSequenceType => HasSuccessWrapper
        ? ((INamedTypeSymbol)PayloadType!).TypeArguments[0] : PayloadType;

    public bool IsStream => StreamSequenceType is INamedTypeSymbol { IsGenericType: true } sequence
        && sequence.OriginalDefinition.ToDisplayString() == ApplicationServiceHelpers.StreamingResultFqn;

    public ITypeSymbol? StreamItemType => IsStream ? ((INamedTypeSymbol)StreamSequenceType!).TypeArguments[0] : null;

    public string ResponseTypeDisplay
        => ValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

}
/// <summary>Represents HTTP verbs while analyzing RPC declarations.</summary>

public enum HttpVerb
{
    Get,
    Post,
    Put,
    Delete,
    Patch
}
/// <summary>Represents HTTP RPC styles while analyzing service declarations.</summary>

public enum HttpRpcStyle
{
    JsonRpc,
    ControllerRpc
}
/// <summary>Describes the HTTP transport of an RPC service declaration.</summary>

public readonly struct HttpTransportInfo
{
    public HttpTransportInfo(HttpRpcStyle style, string? routePrefix)
    {
        Style = style;
        RoutePrefix = routePrefix;
    }

    public HttpRpcStyle Style { get; }

    public string? RoutePrefix { get; }
}
