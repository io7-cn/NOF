using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Contract.SourceGenerator;
using NOF.Infrastructure.SourceGenerator;
using System.Reflection;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class FilterFailureCaseGeneratorTests
{
    [Theory]
    [InlineData("Denied")]
    [InlineData("Denied", "000403")]
    public void GeneratedFilterCatalog_IsUsableAcrossMetadataAssemblies(string name, string? code = null)
    {
        var declaration = code is null ? $"[FailureCase(\"{name}\")]" : $"[FailureCase(\"{name}\", \"{code}\")]";
        var compilation = Create($$"""
            #nullable enable
            using NOF.Contract;
            using NOF.Infrastructure;
            using NOF.Application;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed record Details(int Count);
            {{declaration}}
            [FailureCase("Custom", "Custom", typeof(Details))]
            public sealed partial class Policy : IInvocationFilter
            {
                public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
                    => ValueTask.FromResult<object?>(new Denied("message"));
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new FilterFailureCaseGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics.Concat(output.GetDiagnostics()), item => item.Severity == DiagnosticSeverity.Error);
        var codeText = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.DoesNotContain("public union Result", codeText);
        Assert.DoesNotContain("CreateDenied", codeText);
        using var assemblyStream = new MemoryStream();
        var emitted = output.Emit(assemblyStream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var policy = assembly.GetType("Policy")!;
        var catalog = (IReadOnlyList<InvocationFailureCase>)policy.GetProperty("FailureCases")!.GetValue(null)!;
        var entry = catalog.Single(item => item.CaseType.Name == "Denied");
        Assert.Equal(code ?? name, entry.Title);
        var branch = Activator.CreateInstance(entry.CaseType, "message")!;
        Assert.IsAssignableFrom<IFailure>(branch);
        Assert.Equal("message", entry.ReadDetail(branch));
        var consumer = CSharpCompilation.CreateCompilation("Consumer_" + Guid.NewGuid().ToString("N"), """
            public static class Consumer
            {
                public static object Run() => new Policy.Denied("metadata");
            }
            """, true, ((CSharpCompilation)output).CreateMetadataReference());
        Assert.DoesNotContain(consumer.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("not-valid", "partial", "")]
    [InlineData("404", "partial", "")]
    [InlineData("FailureCases", "partial", "")]
    [InlineData("Describe", "partial", "")]
    [InlineData("Denied", "", "")]
    [InlineData("Denied", "partial", "<T>")]
    public async Task InvalidFilterDeclarations_ReportNOF220(string name, string modifier, string generic)
    {
        var source = $$"""
            using NOF.Contract;
            using NOF.Infrastructure;
            [FailureCase("{{name}}")]
            public abstract {{modifier}} class Policy{{generic}} : IInvocationFilter;
            """;
        var diagnostics = await Create(source).WithAnalyzers([new FailureCaseAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        Assert.Contains(diagnostics, item => item.Id == "NOF220" && item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ValidFilterDeclaration_DefaultAndCustomErrors_NeedNoRuntimeDescriptionMethod()
    {
        var diagnostics = await Create("""
            using NOF.Contract;
            using NOF.Infrastructure;
            [FailureCase("Denied", "000403")]
            [FailureCase("Conflict", "Conflict", typeof(string))]
            public abstract partial class Policy : IInvocationFilter;
            """).WithAnalyzers([new FailureCaseAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        Assert.Empty(diagnostics);
    }

    private static CSharpCompilation Create(string source) => CSharpCompilation.CreateCompilation(
        "FilterCases_" + Guid.NewGuid().ToString("N"), source, true, typeof(IInvocationFilter), typeof(FailureCaseAttribute));
}
