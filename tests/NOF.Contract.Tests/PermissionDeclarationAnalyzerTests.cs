using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Contract.SourceGenerator;
using System.Collections.Immutable;
using Xunit;

namespace NOF.Contract.Tests;

public sealed class PermissionDeclarationAnalyzerTests
{
    [Theory]
    [InlineData("[RequirePermission]", "NOF240")]
    [InlineData("[RequirePermission(\"\")]", "NOF240")]
    [InlineData("[RequirePermission(null)]", "NOF240")]
    [InlineData("[RequirePermission(\"read\", Match = (PermissionMatch)2)]", "NOF240")]
    [InlineData("[AllowAnonymous, RequireAuthenticated]", "NOF241")]
    [InlineData("[AllowAnonymous, RequirePermission(\"read\")]", "NOF241")]
    public async Task InvalidDeclarations_ReportCompileTimeErrors(string attributes, string id)
    {
        var diagnostics = await Analyze($"using NOF.Abstraction; using NOF.Contract; {attributes} public interface IService;");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == id && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ValidGroupsAndBranchLocalAnonymous_DoNotConflict()
    {
        var diagnostics = await Analyze("""
            using NOF.Abstraction; using NOF.Contract;
            [RequirePermission("read")]
            public interface IService
            {
                [RequirePermission("write"), RequirePermission("owner", "admin", Match = PermissionMatch.Any)]
                void Update();
                [AllowAnonymous] void Public();
                [RequireAuthenticated] void Login();
            }
            """);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ImplementationMethod_IsRejectedEvenWhenAttributeTargetAllowsMethods()
    {
        var diagnostics = await Analyze("using NOF.Abstraction; using NOF.Contract; public sealed class Handler { [RequireAuthenticated] public void Handle() {} }");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF242");
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(string source)
        => CSharpCompilation.CreateCompilation("Permissions", source, true, typeof(RequirePermissionAttribute), typeof(Abstraction.PermissionMatch))
            .WithAnalyzers([new PermissionDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();
}
