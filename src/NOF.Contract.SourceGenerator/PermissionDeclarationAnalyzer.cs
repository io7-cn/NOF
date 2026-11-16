using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Contract.SourceGenerator;

/// <summary>Validates built-in permission declarations independently of runtime or generated operation types.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PermissionDeclarationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Reports empty permission groups, blank identifiers or invalid match values.</summary>
    public static readonly DiagnosticDescriptor InvalidPermission = new("NOF240", "Invalid permission declaration",
        "'{0}' must declare nonblank permissions and a valid PermissionMatch; use RequireAuthenticated for login-only access",
        "NOF.Authorization", DiagnosticSeverity.Error, true);
    /// <summary>Reports contradictory declarations on one source member.</summary>
    public static readonly DiagnosticDescriptor ConflictingAnonymous = new("NOF241", "Conflicting anonymous declaration",
        "'{0}' cannot combine AllowAnonymous with authentication or permission requirements on the same declaration",
        "NOF.Authorization", DiagnosticSeverity.Error, true);
    /// <summary>Reports declarations on implementations instead of Contract interfaces.</summary>
    public static readonly DiagnosticDescriptor InvalidTarget = new("NOF242", "Permissions belong to Contract interfaces",
        "'{0}' must declare permissions on a Contract interface or its operation method, not an implementation",
        "NOF.Authorization", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [InvalidPermission, ConflictingAnonymous, InvalidTarget];
    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType, SymbolKind.Method);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var attributes = context.Symbol.GetAttributes();
        var requirements = attributes.Where(attribute => attribute.AttributeClass?.ToDisplayString() == "NOF.Contract.RequirePermissionAttribute").ToArray();
        var authenticated = attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() == "NOF.Contract.RequireAuthenticatedAttribute");
        var anonymous = attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() == "NOF.Contract.AllowAnonymousAttribute");
        if ((authenticated || anonymous || requirements.Length > 0)
            && (context.Symbol is INamedTypeSymbol named ? named.TypeKind != TypeKind.Interface : context.Symbol.ContainingType?.TypeKind != TypeKind.Interface))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidTarget, context.Symbol.Locations.FirstOrDefault(), context.Symbol.Name));
        }
        if ((authenticated || requirements.Length > 0)
            && attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() == "NOF.Contract.AllowAnonymousAttribute"))
        {
            context.ReportDiagnostic(Diagnostic.Create(ConflictingAnonymous, context.Symbol.Locations.FirstOrDefault(), context.Symbol.Name));
        }
        foreach (var requirement in requirements)
        {
            var valid = requirement.ConstructorArguments.Length == 1 && !requirement.ConstructorArguments[0].IsNull
                && requirement.ConstructorArguments[0].Kind == TypedConstantKind.Array
                && requirement.ConstructorArguments[0].Values.Length > 0
                && requirement.ConstructorArguments[0].Values.All(value => value.Value is string id && !string.IsNullOrWhiteSpace(id));
            var match = requirement.NamedArguments.FirstOrDefault(value => value.Key == "Match");
            if (match.Key is not null && (match.Value.Value is not int value || value is not (0 or 1)))
            {
                valid = false;
            }
            if (!valid)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidPermission,
                    requirement.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? context.Symbol.Locations.FirstOrDefault(), context.Symbol.Name));
            }
        }
    }
}
