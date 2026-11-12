using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.SourceGeneration;
using System.Collections.Immutable;
using System.Linq;

namespace NOF.Contract.SourceGenerator;

/// <summary>Rejects discarded expected outcomes and incomplete or catch-all switch handling.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExpectedOutcomeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>A checked result must be consumed or propagated.</summary>
    public static readonly DiagnosticDescriptor IgnoredOutcome = new("NOF230", "Expected outcome ignored",
        "Consume or return the expected outcome instead of discarding it", "ExpectedFailures", DiagnosticSeverity.Error, true);

    /// <summary>Expected outcome switch expressions must remain exhaustive.</summary>
    public static readonly DiagnosticDescriptor IncompleteOutcome = new("NOF231", "Expected outcome handling is incomplete",
        "Handle every declared case using an exhaustive switch expression or Match", "ExpectedFailures", DiagnosticSeverity.Error, true);

    /// <summary>A catch-all hides new declared failures from exhaustiveness analysis.</summary>
    public static readonly DiagnosticDescriptor CatchAllOutcome = new("NOF232", "Catch-all hides expected failures",
        "Use explicit case types and, if needed, a null arm instead of a catch-all", "ExpectedFailures", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [IgnoredOutcome, IncompleteOutcome, CatchAllOutcome, FailureCaseGenerator.InvalidDeclaration];

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
                analysis.ReportDiagnostic(Diagnostic.Create(FailureCaseGenerator.InvalidDeclaration,
                    method.Locations.FirstOrDefault(), "Declare FailureCase on the IApplicationService contract method, not its implementation."));
            }
        }, SymbolKind.Method);
        context.RegisterSyntaxNodeAction(AnalyzeDiscard, SyntaxKind.ExpressionStatement, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeSwitch, SyntaxKind.SwitchExpression, SyntaxKind.SwitchStatement);
    }

    private static bool IsExpected(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { Arity: 1 } named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && named.Name is "Task" or "ValueTask")
        {
            type = named.TypeArguments[0];
        }
        return type?.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "NOF.Contract.ExpectedOutcomeAttribute") == true;
    }

    private static void AnalyzeDiscard(SyntaxNodeAnalysisContext context)
    {
        var expression = context.Node switch
        {
            ExpressionStatementSyntax { Expression: not AssignmentExpressionSyntax } statement => statement.Expression,
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "_" } } assignment
                when context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is null or IDiscardSymbol => assignment.Right,
            _ => null
        };
        if (expression != null && IsExpected(context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(IgnoredOutcome, expression.GetLocation()));
        }
    }

    private static void AnalyzeSwitch(SyntaxNodeAnalysisContext context)
    {
        var expression = context.Node is SwitchExpressionSyntax value ? value.GoverningExpression
            : ((SwitchStatementSyntax)context.Node).Expression;
        if (!IsExpected(context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type))
        {
            return;
        }
        if (context.Node is not SwitchExpressionSyntax switchExpression)
        {
            context.ReportDiagnostic(Diagnostic.Create(IncompleteOutcome, context.Node.GetLocation()));
            return;
        }
        if (context.SemanticModel.GetDiagnostics(switchExpression.Span, context.CancellationToken)
            .Any(diagnostic => diagnostic.Id is "CS8509" or "CS8655"))
        {
            context.ReportDiagnostic(Diagnostic.Create(IncompleteOutcome, switchExpression.GetLocation()));
        }
        foreach (var arm in switchExpression.Arms)
        {
            if (HidesCases(arm.Pattern, context))
            {
                context.ReportDiagnostic(Diagnostic.Create(CatchAllOutcome, arm.Pattern.GetLocation()));
            }
        }
    }

    private static bool HidesCases(PatternSyntax pattern, SyntaxNodeAnalysisContext context)
        => pattern switch
        {
            DiscardPatternSyntax or VarPatternSyntax or UnaryPatternSyntax => true,
            RecursivePatternSyntax { Type: null } => true,
            ParenthesizedPatternSyntax parenthesized => HidesCases(parenthesized.Pattern, context),
            BinaryPatternSyntax binary => HidesCases(binary.Left, context) || HidesCases(binary.Right, context),
            TypePatternSyntax typed => context.SemanticModel.GetTypeInfo(typed.Type, context.CancellationToken).Type?.SpecialType == SpecialType.System_Object,
            DeclarationPatternSyntax declared => context.SemanticModel.GetTypeInfo(declared.Type, context.CancellationToken).Type?.SpecialType == SpecialType.System_Object,
            RecursivePatternSyntax { Type: { } type } => context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type?.SpecialType == SpecialType.System_Object,
            _ => false
        };
}
