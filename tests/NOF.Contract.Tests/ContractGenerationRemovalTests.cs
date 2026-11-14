using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Contract.SourceGenerator;
using Xunit;

namespace NOF.Contract.Tests;

public sealed class ContractGenerationRemovalTests
{
    [Fact]
    public async Task ContractCompilation_UsesOnlyDeclarationAnalyzers_WithoutGeneratedCode()
    {
        var assembly = typeof(ApplicationServiceAnalyzer).Assembly;
        Assert.Equal("NOF.Contract.SourceGenerator", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetTypes(), type => typeof(IIncrementalGenerator).IsAssignableFrom(type)
            || typeof(ISourceGenerator).IsAssignableFrom(type) || type.IsDefined(typeof(GeneratorAttribute), false));
        const string source = """
            using NOF.Contract;
            public sealed record Query;
            public interface IService : IApplicationService
            {
                [FailureCase("Rejected", "404")]
                Empty Run(Query request);
            }
            """;
        var compilation = CSharpCompilation.CreateCompilation("DeclarationsOnly", source, true, typeof(IApplicationService));
        compilation = compilation.RemoveReferences(compilation.References.Where(reference =>
            Path.GetFileNameWithoutExtension(reference.Display) is { } name
            && name.StartsWith("NOF.", StringComparison.Ordinal) && name != "NOF.Contract"));
        var diagnostics = await compilation.WithAnalyzers([new ApplicationServiceAnalyzer(), new FailureCaseAnalyzer()])
            .GetAllDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilation.GetTypeByMetadataName("IService")!.GetTypeMembers());
        Assert.Null(compilation.GetTypeByMetadataName("IServiceOperations"));
    }
}
