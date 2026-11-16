using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NOF.SourceGeneration;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace NOF.Application.SourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApplicationServiceAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ApplicationServiceMustBePartial = new(
        "NOF300",
        "RPC server class must be partial",
        "Class '{0}' inheriting ApplicationService<TService> must be declared partial",
        "ApplicationService",
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        ApplicationServiceMustBePartial
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || !ApplicationServiceSymbolHelper.TryGetServiceInterface(type, out _))
        {
            return;
        }

        var location = type.Locations.FirstOrDefault() ?? Location.None;
        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
        if (!isPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(ApplicationServiceMustBePartial, location, type.Name));
        }
    }
}
