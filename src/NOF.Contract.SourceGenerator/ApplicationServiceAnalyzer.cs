using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Contract.SourceGenerator;

/// <summary>Validates transport-independent proto-style application service signatures.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApplicationServiceAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor RequestMustBeReferenceType = new(
        "NOF200",
        "Request must be a reference type",
        "Request type '{0}' must be a class or record, not a struct",
        "ApplicationService",
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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [RequestMustBeReferenceType, InvalidServiceMethodSignature, ServiceMethodOverloadsNotSupported, VoidReturnNotSupported];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var service = (INamedTypeSymbol)context.Symbol;
        if (service.TypeKind != TypeKind.Interface || !service.AllInterfaces.Any(static contract =>
            contract.ToDisplayString() == "NOF.Contract.IApplicationService"))
        {
            return;
        }
        var methods = service.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared).ToArray();
        foreach (var group in methods.GroupBy(static method => method.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1))
        {
            foreach (var method in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(ServiceMethodOverloadsNotSupported, method.Locations.FirstOrDefault(), method.Name, service.Name));
            }
        }
        foreach (var method in methods)
        {
            var location = method.Locations.FirstOrDefault();
            if (method.ReturnsVoid)
            {
                context.ReportDiagnostic(Diagnostic.Create(VoidReturnNotSupported, location, method.Name, service.Name));
                continue;
            }
            var returnName = method.ReturnType.OriginalDefinition.ToDisplayString();
            if (method.Parameters.Length != 1 || method.Name.EndsWith("Async", StringComparison.Ordinal)
                || method.Parameters.Any(static parameter => parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
                || returnName is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
                    or "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>"
                    or "System.Threading.Tasks.Task<T>" or "System.Threading.Tasks.ValueTask<T>")
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidServiceMethodSignature, location, method.Name, service.Name));
                continue;
            }
            if (!method.Parameters[0].Type.IsReferenceType)
            {
                context.ReportDiagnostic(Diagnostic.Create(RequestMustBeReferenceType, location, method.Parameters[0].Type.Name));
            }
        }
    }
}
