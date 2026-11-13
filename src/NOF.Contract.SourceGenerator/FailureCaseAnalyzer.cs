using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.SourceGeneration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Contract.SourceGenerator;

/// <summary>Validates expected-failure declarations without adding members to contracts.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FailureCaseAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Reports invalid expected-failure declarations.</summary>
    public static readonly DiagnosticDescriptor InvalidDeclaration = new(
        "NOF220", "Invalid expected failure declaration", "{0}", "ExpectedFailures", DiagnosticSeverity.Error, true);
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [InvalidDeclaration];
    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(static analysis =>
        {
            var method = (IMethodSymbol)analysis.Symbol;
            if (ExpectedFailureModel.Declarations(method).Length > 0 && method.ContainingType.TypeKind != TypeKind.Interface)
            {
                Report(analysis, method, "Declare FailureCase on the IApplicationService contract method, not its implementation.");
            }
        }, SymbolKind.Method);
        context.RegisterSymbolAction(static analysis => AnalyzeService(analysis, (INamedTypeSymbol)analysis.Symbol), SymbolKind.NamedType);
    }

    private static void AnalyzeService(SymbolAnalysisContext output, INamedTypeSymbol service)
    {
        var filterDeclarations = service.GetAttributes().Where(ExpectedFailureModel.IsDeclaration).ToArray();
        if (filterDeclarations.Length > 0)
        {
            var isFilter = service.TypeKind == TypeKind.Class && service.AllInterfaces.Any(type => type.ToDisplayString() == "NOF.Infrastructure.IInvocationFilter");
            var partial = service.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ClassDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isFilter || !partial || service.ContainingType is not null || service.Arity != 0)
            {
                Report(output, service, "FailureCase on a class requires a top-level, non-generic partial IInvocationFilter.");
            }
            ValidateDeclarations(output, service, filterDeclarations, service.Name);
            return;
        }
        var methods = service.GetMembers().OfType<IMethodSymbol>()
            .Where(method => ExpectedFailureModel.Declarations(method).Length > 0).ToArray();
        if (methods.Length == 0)
        {
            return;
        }
        if (service.ContainingType != null || service.Arity > 0
            || !service.AllInterfaces.Any(type => type.ToDisplayString() == "NOF.Contract.IApplicationService"))
        {
            Report(output, service, "FailureCase requires a top-level, non-generic IApplicationService interface.");
            return;
        }
        foreach (var method in methods)
        {
            var declarations = ExpectedFailureModel.Declarations(method);
            var nullable = method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated
                || method.ReturnType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
            if (method.IsStatic || method.Parameters.Length != 1 || method.TypeParameters.Length != 0 || method.ReturnsVoid || nullable
                || method.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.", StringComparison.Ordinal)
                || service.GetMembers(method.Name).OfType<IMethodSymbol>().Count() != 1)
            {
                Report(output, method, "FailureCase methods require one request, a non-nullable synchronous success type and a unique operation name.");
                continue;
            }
            ValidateDeclarations(output, method, declarations, method.Name);
        }
    }

    private static void ValidateDeclarations(SymbolAnalysisContext output, ISymbol owner, AttributeData[] declarations, string ownerName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            var name = ExpectedFailureModel.CaseName(declaration);
            if (!ExpectedFailureModel.ValidName(name)
                || !ExpectedFailureModel.HasCustomError(declaration) && string.IsNullOrWhiteSpace(ExpectedFailureModel.Code(declaration))
                || ExpectedFailureModel.HasCustomError(declaration) && (ExpectedFailureModel.ErrorType(declaration) is not { } errorType
                    || errorType.TypeKind == TypeKind.Error || errorType.SpecialType == SpecialType.System_Void
                    || errorType is INamedTypeSymbol { IsUnboundGenericType: true }
                    || errorType.IsRefLikeType || errorType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                || name == ownerName || name is "FailureCases" or "Describe" or "InvokeAsync" or "Result" or "RequestType" or "ResponseType" or "AllowsNullResponse" or "HandleAsync" or "Descriptor" or "Match" or "JsonClassifier" or "ErrorCode" or "Message" or "Error" or "Kind"
                    or "Value" or "Equals" or "GetHashCode" or "ToString" or "GetType" or "Deconstruct" or "EqualityContract" or "PrintMembers"
                || !names.Add(name)
                || declaration.AttributeClass?.TypeArguments.Any(type => type.TypeKind == TypeKind.Error) == true)
            {
                Report(output, owner, "Each FailureCase requires a unique valid C# name that does not collide with operation members. The optional second argument is either a nonblank error code or a closed usable error data type; omitting it defaults the error code to the name.");
            }
        }
    }

    private static void Report(SymbolAnalysisContext output, ISymbol symbol, string message)
        => output.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, symbol.Locations.FirstOrDefault(), message));

}
