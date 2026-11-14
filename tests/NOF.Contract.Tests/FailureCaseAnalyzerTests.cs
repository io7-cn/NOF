using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Contract.SourceGenerator;
using System.Collections.Immutable;
using Xunit;

namespace NOF.Contract.Tests;

public sealed class FailureCaseAnalyzerTests
{
    [Theory]
    [InlineData("[FailureCase(\"\")]", "string")]
    [InlineData("[FailureCase(\"Same\")][FailureCase(\"Same\", \"B\")]", "string")]
    [InlineData("[FailureCase(\"Match\")]", "string")]
    [InlineData("[FailureCase(\"Value\")]", "string")]
    [InlineData("[FailureCase(\"Descriptor\")]", "string")]
    [InlineData("[FailureCase(\"Result\")]", "string")]
    [InlineData("[FailureCase(\"var\")]", "string")]
    [InlineData("[FailureCase(\"not-valid\")]", "string")]
    [InlineData("[FailureCase(\"404\")]", "string")]
    [InlineData("[FailureCase(\"class\")]", "string")]
    [InlineData("[FailureCase(\"NotFound\", \" \")]", "string")]
    [InlineData("[FailureCase(\"Custom\", typeof(void))]", "string")]
    [InlineData("[FailureCase(\"Custom\", typeof(System.Collections.Generic.List<>))]", "string")]
    [InlineData("[FailureCase(\"A\")]", "string?")]
    public async Task InvalidDeclarations_ReportNOF220_WithoutRequiringPartial(string attributes, string response)
    {
        var source = $$"""
            #nullable enable
            using NOF.Contract;
            public sealed record Query;
            public interface IService : IApplicationService
            {
                {{attributes}}
                {{response}} Run(Query request);
            }
            """;
        Assert.Contains(await Analyze(source), diagnostic => diagnostic.Id == "NOF220" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ImplementationAnnotations_ReportDeclarationError()
    {
        Assert.Contains(await Analyze("""
            using NOF.Contract;
            public sealed class Handler
            {
                [FailureCase("Missing")]
                public string Run(object input) => "ok";
            }
            """), diagnostic => diagnostic.Id == "NOF220");
    }

    [Fact]
    public async Task ValidNonPartialContract_CompilesWithoutGeneratedOutcomeMembers()
    {
        const string source = """
            using NOF.Contract;
            public interface IService : IApplicationService
            {
                [FailureCase("Missing", "000404")]
                [FailureCase("Invalid", "Invalid", typeof(string))]
                Empty Run(object request);
            }
            """;
        var compilation = Create(source);
        Assert.Empty(await Analyze(source));
        Assert.Empty(compilation.GetTypeByMetadataName("IService")!.GetTypeMembers());
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(typeof(IApplicationService).Assembly.GetReferencedAssemblies(), reference => reference.Name == "NOF.Application");
    }

    private static CSharpCompilation Create(string source)
        => CSharpCompilation.CreateCompilation("FailureDeclarations", source, true, typeof(FailureCaseAttribute));

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
        => await Create(source).WithAnalyzers([new FailureCaseAnalyzer()]).GetAnalyzerDiagnosticsAsync();
}
