using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NOF.CodeFixes;
using NOF.Domain.SourceGenerator;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class MakeCollectionOwnerPartialCodeFixTests
{
    [Fact]
    public async Task CodeFix_AddsPartialToTheReportedDeclaration()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("CollectionFix", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Aggregate.cs", Microsoft.CodeAnalysis.Text.SourceText.From("public class Aggregate { }"));
        var root = (await document.GetSyntaxRootAsync())!;
        var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var diagnostic = Diagnostic.Create(ReadOnlyCollectionGenerator.RequiresPartial, declaration.Identifier.GetLocation(), "Aggregate");
        var actions = new List<CodeAction>();
        await new MakeCollectionOwnerPartialCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default));
        var operations = await Assert.Single(actions).GetOperationsAsync(default);
        var change = Assert.Single(operations.OfType<ApplyChangesOperation>());
        var updated = (await change.ChangedSolution.GetDocument(document.Id)!.GetSyntaxRootAsync())!;
        Assert.Contains(updated.DescendantNodes().OfType<ClassDeclarationSyntax>().Single().Modifiers,
            token => token.IsKind(SyntaxKind.PartialKeyword));
    }
}
