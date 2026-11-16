using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace NOF.CodeFixes;

/// <summary>Adds partial to a collection owner when requested by NOF021.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeCollectionOwnerPartialCodeFixProvider)), Shared]
public sealed class MakeCollectionOwnerPartialCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ["NOF021"];
    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;
    /// <inheritdoc />
    public override async System.Threading.Tasks.Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span).FirstAncestorOrSelf<ClassDeclarationSyntax>() is not { } declaration)
        {
            return;
        }
        context.RegisterCodeFix(CodeAction.Create("Make collection owner partial", _ =>
            System.Threading.Tasks.Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(declaration,
                declaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword))))), equivalenceKey: "MakeCollectionOwnerPartial"), context.Diagnostics);
    }
}
