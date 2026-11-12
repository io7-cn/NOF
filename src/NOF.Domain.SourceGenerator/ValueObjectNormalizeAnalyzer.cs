using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Domain.SourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueObjectNormalizeAnalyzer : DiagnosticAnalyzer
{
    private const string InterfaceMetadataName = "NOF.Domain.IValueObject<T>";

    public static readonly DiagnosticDescriptor HookMustNotCallFactory = new(
        id: "NOF013",
        title: "Value object hooks must not call factories",
        messageFormat: "'{0}.{1}' must not call '{0}.{2}(...)' because it causes recursive value object construction",
        category: "ValueObjectGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NormalizeShouldNotCallIsValid = new(
        id: "NOF014",
        title: "Normalize should not call IsValid",
        messageFormat: "'{0}.Normalize' should not call '{0}.IsValid(...)' to keep normalization and validation separate",
        category: "ValueObjectGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>Rejects the removed throwing validation hook to prevent silently skipped validation.</summary>
    public static readonly DiagnosticDescriptor RemovedValidationHook = new(
        "NOF019", "Validate is no longer a value object hook",
        "Replace '{0}.Validate' with bool IsValid; use From or TryFrom to construct the value object",
        "ValueObjectGenerator", DiagnosticSeverity.Error, true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [HookMustNotCallFactory, NormalizeShouldNotCallIsValid, RemovedValidationHook];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodSyntax = (MethodDeclarationSyntax)context.Node;
        if (methodSyntax.Identifier.ValueText is not ("Normalize" or "IsValid" or "Validate"))
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken) is not IMethodSymbol methodSymbol ||
            !methodSymbol.IsStatic ||
            methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
            !containingType.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == InterfaceMetadataName))
        {
            return;
        }

        if (methodSymbol.Name == "Validate")
        {
            context.ReportDiagnostic(Diagnostic.Create(RemovedValidationHook, methodSyntax.Identifier.GetLocation(), containingType.Name));
            return;
        }

        foreach (var invocation in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (TryGetCalledMethodName(invocation, context, containingType) is not string methodName)
            {
                continue;
            }

            if (methodName is "From" or "TryFrom")
            {
                context.ReportDiagnostic(Diagnostic.Create(HookMustNotCallFactory, invocation.GetLocation(), containingType.Name, methodSymbol.Name, methodName));
            }
            else if (methodSymbol.Name == "Normalize" && methodName == "IsValid")
            {
                context.ReportDiagnostic(Diagnostic.Create(NormalizeShouldNotCallIsValid, invocation.GetLocation(), containingType.Name));
            }
        }
    }

    private static string? TryGetCalledMethodName(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol containingType)
    {
        switch (invocation.Expression)
        {
            case IdentifierNameSyntax identifierName:
                return identifierName.Identifier.ValueText is "From" or "TryFrom" or "IsValid"
                    ? identifierName.Identifier.ValueText
                    : null;

            case MemberAccessExpressionSyntax memberAccess:
                if (memberAccess.Name.Identifier.ValueText is not ("From" or "TryFrom" or "IsValid"))
                {
                    return null;
                }

                var targetSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol;
                if (SymbolEqualityComparer.Default.Equals(targetSymbol, containingType))
                {
                    return memberAccess.Name.Identifier.ValueText;
                }

                return null;

            default:
                return null;
        }
    }
}
