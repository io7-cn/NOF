using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Hosting.SourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HttpApplicationServiceAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor RequestMustBeReferenceType = new(
        "NOF200",
        "Request must be a reference type",
        "Request type '{0}' must be a class or record, not a struct",
        "HttpEndpoint",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor RouteParametersNotSupported = new(
        "NOF201",
        "Route parameters are not supported",
        "HttpEndpoint route '{0}' must not contain route parameters. Put input data on the request object instead.",
        "HttpEndpoint",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidRouteBinding = new(
        "NOF217",
        "Invalid HTTP route binding",
        "HttpEndpoint route '{0}' cannot be bound: {1}",
        "HttpEndpoint",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ClassMustHaveParameterlessCtor = new(
        "NOF202",
        "Class request must have a parameterless constructor",
        "Request class '{0}' must have a public parameterless constructor. Records may use primary constructors, but classes must have a parameterless constructor.",
        "HttpEndpoint",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor QueryPropertyMustBeParsable = new(
        "NOF203",
        "Query-bound property type must be parsable",
        "Property '{0}' on request type '{1}' is bound from query and must be string, an enum, a TryParse-compatible type, or implement IParsable<TSelf>",
        "HttpEndpoint",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidServiceMethodSignature = new(
        "NOF207",
        "Invalid service method signature",
        "Method '{0}' on service interface '{1}' must have exactly 1 request parameter, must not declare CancellationToken, must not end with Async, and must return a non-Task value",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ServiceMethodOverloadsNotSupported = new(
        "NOF208",
        "Service method overloading is not supported",
        "Service interface '{1}' contains overloaded methods named '{0}'. Overloading is not supported; use unique method names.",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor VoidReturnNotSupported = new(
        "NOF209",
        "Void return is not supported",
        "Method '{0}' on service interface '{1}' must not return void. Return a business value, such as Result when no payload is needed.",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor TransportRequiresRpcService = new(
        "NOF211",
        "RPC transport can only be declared on an RPC service contract",
        "Transport attribute '{0}' can only be applied to an interface inheriting IApplicationService",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleTransportsNotSupported = new(
        "NOF212",
        "Only one RPC transport can be declared",
        "RPC service contract '{0}' declares multiple TransportOverAttribute implementations; declare exactly one transport",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor HttpEndpointNotSupportedForJsonRpc = new(
        "NOF213",
        "HttpEndpoint is not supported by JSON-RPC contracts",
        "Method '{0}' on JSON-RPC service '{1}' must not declare HttpEndpointAttribute; JSON-RPC uses the contract route prefix and operation name",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidHttpRoutePrefix = new(
        "NOF215",
        "HTTP RPC route prefix is invalid",
        "TransportOverHttp route prefix '{0}' on RPC service contract '{1}' is invalid: {2}",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor HttpEndpointNotSupportedForMemory = new(
        "NOF216",
        "HttpEndpoint is not supported by in-memory RPC contracts",
        "Method '{0}' on in-memory RPC service '{1}' must not declare HttpEndpointAttribute; in-memory services are not exposed over HTTP",
        "RpcService",
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        RequestMustBeReferenceType,
        RouteParametersNotSupported,
        InvalidRouteBinding,
        ClassMustHaveParameterlessCtor,
        QueryPropertyMustBeParsable,
        TransportRequiresRpcService,
        MultipleTransportsNotSupported,
        HttpEndpointNotSupportedForJsonRpc,
        InvalidHttpRoutePrefix,
        HttpEndpointNotSupportedForMemory
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        AnalyzeLegacyHttpEndpointAttributes(context, typeSymbol);
        AnalyzeTransportAttributes(context, typeSymbol);
        AnalyzeRpcServiceInterface(context, typeSymbol);
    }

    private static void AnalyzeTransportAttributes(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol)
    {
        var transportAttributes = typeSymbol.GetAttributes()
            .Where(static attribute => IsTransportOverAttribute(attribute.AttributeClass))
            .ToArray();
        if (transportAttributes.Length == 0)
        {
            return;
        }

        var typeLocation = typeSymbol.Locations.FirstOrDefault() ?? Location.None;
        if (!ApplicationServiceHelpers.IsApplicationServiceInterface(typeSymbol))
        {
            foreach (var attribute in transportAttributes)
            {
                var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? typeLocation;
                context.ReportDiagnostic(Diagnostic.Create(
                    TransportRequiresRpcService,
                    attributeLocation,
                    attribute.AttributeClass?.Name ?? "TransportOverAttribute"));
            }

            return;
        }

        if (transportAttributes.Length > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(MultipleTransportsNotSupported, typeLocation, typeSymbol.Name));
        }

        foreach (var attribute in transportAttributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() != ApplicationServiceHelpers.TransportOverHttpAttributeFqn
                || attribute.ConstructorArguments.Length < 2
                || attribute.ConstructorArguments[1].Value is not string routePrefix
                || HttpRoutePrefix.TryNormalize(routePrefix, out _, out var error))
            {
                continue;
            }

            var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeLocation;
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidHttpRoutePrefix,
                attributeLocation,
                routePrefix,
                typeSymbol.Name,
                error));
        }
    }

    private static void AnalyzeLegacyHttpEndpointAttributes(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol)
    {
        var httpEndpointAttributes = typeSymbol.GetAttributes()
            .Where(attr => attr.AttributeClass?.ToDisplayString() == ApplicationServiceHelpers.HttpEndpointAttributeFqn)
            .ToList();

        if (httpEndpointAttributes.Count == 0)
        {
            return;
        }

        var typeLocation = typeSymbol.Locations.FirstOrDefault() ?? Location.None;
        ValidateRequestPayloadShape(context, typeSymbol, typeLocation);

        foreach (var attr in httpEndpointAttributes)
        {
            var attrLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? typeLocation;
            ValidateQueryBoundProperties(context, typeSymbol, [attr], attrLocation);
            var route = attr.ConstructorArguments.Length > 1 ? attr.ConstructorArguments[1].Value as string : null;
            if (string.IsNullOrWhiteSpace(route))
            {
                continue;
            }

            ValidateRoute(context, route!, attrLocation);
        }
    }

    private static void AnalyzeRpcServiceInterface(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol)
    {
        if (!ApplicationServiceHelpers.IsApplicationServiceInterface(typeSymbol))
        {
            return;
        }
        var attrLocation = typeSymbol.Locations.FirstOrDefault() ?? Location.None;
        var isJsonRpc = ApplicationServiceHelpers.TryGetHttpTransport(typeSymbol, out var transport)
            && transport.Style == HttpRpcStyle.JsonRpc;
        var isMemory = ApplicationServiceHelpers.HasMemoryTransport(typeSymbol);

        var declaredMethods = typeSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared);
        foreach (var method in declaredMethods)
        {
            var methodHttpEndpointAttrs = method.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == ApplicationServiceHelpers.HttpEndpointAttributeFqn)
                .ToArray();
            if (isJsonRpc || isMemory)
            {
                foreach (var methodHttpEndpointAttr in methodHttpEndpointAttrs)
                {
                    var attributeLocation = methodHttpEndpointAttr.ApplicationSyntaxReference
                        ?.GetSyntax(context.CancellationToken)
                        .GetLocation()
                        ?? method.Locations.FirstOrDefault()
                        ?? attrLocation;
                    context.ReportDiagnostic(Diagnostic.Create(
                        isMemory ? HttpEndpointNotSupportedForMemory : HttpEndpointNotSupportedForJsonRpc,
                        attributeLocation,
                        method.Name,
                        typeSymbol.Name));
                }
            }

            if (!ApplicationServiceHelpers.TryGetRequestParameter(method, out var requestParameter)
                || !IsNonTaskReturnType(method) || requestParameter?.Type is not INamedTypeSymbol requestType)
            {
                continue;
            }

            if (methodHttpEndpointAttrs.Length == 0 || isJsonRpc || isMemory)
            {
                continue;
            }

            var methodLocation = method.Locations.FirstOrDefault() ?? attrLocation;
            if (requestType != null)
            {
                ValidateRequestPayloadShape(context, requestType, methodLocation, reportReferenceType: false);
                ValidateQueryBoundProperties(context, requestType, methodHttpEndpointAttrs.Where(static attr =>
                    attr.ConstructorArguments.Length <= 1
                    || attr.ConstructorArguments[1].Value is not string route
                    || !ApplicationServiceHelpers.ContainsRouteParameters(route)), methodLocation);
                foreach (var methodHttpEndpointAttr in methodHttpEndpointAttrs)
                {
                    var route = methodHttpEndpointAttr.ConstructorArguments.Length > 1
                        ? methodHttpEndpointAttr.ConstructorArguments[1].Value as string
                        : null;
                    if (route is null || !ApplicationServiceHelpers.ContainsRouteParameters(route))
                    {
                        continue;
                    }

                    var verb = (HttpVerb)(int)methodHttpEndpointAttr.ConstructorArguments[0].Value!;
                    if (HttpRpcRouteBinding.Create(method, requestType, verb, route, out var error) is null)
                    {
                        var location = methodHttpEndpointAttr.ApplicationSyntaxReference
                            ?.GetSyntax(context.CancellationToken).GetLocation() ?? methodLocation;
                        context.ReportDiagnostic(Diagnostic.Create(InvalidRouteBinding, location, route, error));
                    }
                }
            }
        }

    }

    private static void ValidateRequestPayloadShape(SymbolAnalysisContext context, INamedTypeSymbol requestType, Location location, bool reportReferenceType = true)
    {
        var requestLocation = requestType.Locations.FirstOrDefault(static candidate => candidate.IsInSource) ?? location;
        if (requestType.IsValueType)
        {
            if (reportReferenceType)
            {
                context.ReportDiagnostic(Diagnostic.Create(RequestMustBeReferenceType, requestLocation, requestType.Name));
            }
            return;
        }

        if (requestType.IsRecord)
        {
            return;
        }

        var hasParameterlessCtor = requestType.Constructors
            .Any(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length == 0);

        var hasExplicitCtors = requestType.Constructors
            .Any(c => !c.IsStatic && !c.IsImplicitlyDeclared);

        if (hasExplicitCtors && !hasParameterlessCtor)
        {
            context.ReportDiagnostic(Diagnostic.Create(ClassMustHaveParameterlessCtor, requestLocation, requestType.Name));
        }
    }

    private static void ValidateRoute(
        SymbolAnalysisContext context,
        string route,
        Location location)
    {
        if (ApplicationServiceHelpers.ContainsRouteParameters(route))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(RouteParametersNotSupported, location, route));
        }
    }

    private static void ValidateQueryBoundProperties(
        SymbolAnalysisContext context,
        INamedTypeSymbol requestType,
        IEnumerable<AttributeData> endpointAttributes,
        Location location)
    {
        var hasQueryBinding = endpointAttributes.Any(static attr =>
            attr.ConstructorArguments.Length > 0
            && attr.ConstructorArguments[0].Value is int verb
            && (verb == (int)HttpVerb.Get || verb == (int)HttpVerb.Delete));

        if (!hasQueryBinding)
        {
            return;
        }

        foreach (var property in ApplicationServiceHelpers.GetAllPublicProperties(requestType))
        {
            if (!IsQueryStringParsable(property.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    QueryPropertyMustBeParsable,
                    property.Locations.FirstOrDefault() ?? location,
                    property.Name,
                    requestType.Name));
            }
        }
    }

    private static bool IsQueryStringParsable(ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol nullableReference)
        {
            type = nullableReference;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            if (namedType.AllInterfaces.Any(static i =>
                    i.OriginalDefinition.ToDisplayString() == ApplicationServiceHelpers.ParsableFqn))
            {
                return true;
            }

            return namedType.GetMembers("TryParse")
                .OfType<IMethodSymbol>()
                .Any(method =>
                {
                    if (method.Parameters.Length < 2)
                    {
                        return false;
                    }

                    var resultParameter = method.Parameters[method.Parameters.Length - 1];
                    return method.IsStatic
                        && method.DeclaredAccessibility == Accessibility.Public
                        && method.ReturnType.SpecialType == SpecialType.System_Boolean
                        && method.Parameters[0].Type.SpecialType == SpecialType.System_String
                        && resultParameter.RefKind == RefKind.Out
                        && SymbolEqualityComparer.Default.Equals(resultParameter.Type, type);
                });
        }

        return false;
    }

    private static bool IsNonTaskReturnType(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
        {
            return false;
        }

        var returnType = method.ReturnType;
        if (returnType.ToDisplayString() is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.ValueTask")
        {
            return false;
        }

        return returnType is not INamedTypeSymbol { IsGenericType: true } namedType
               || (namedType.OriginalDefinition.ToDisplayString() != "System.Threading.Tasks.Task<T>"
                   && namedType.OriginalDefinition.ToDisplayString() != "System.Threading.Tasks.ValueTask<T>");
    }

    private static bool IsTransportOverAttribute(INamedTypeSymbol? attributeType)
    {
        for (var current = attributeType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == ApplicationServiceHelpers.TransportOverAttributeFqn)
            {
                return true;
            }
        }

        return false;
    }

}
