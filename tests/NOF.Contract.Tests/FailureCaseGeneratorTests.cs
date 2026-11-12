using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Contract.SourceGenerator;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;
using Xunit;

namespace NOF.Contract.Tests;

public sealed class FailureCaseGeneratorTests
{
    private const string Contract = """
        using NOF.Contract;
        using System.Text.Json;
        public sealed record Query(long Id);
        public sealed record ConflictDetails(long NodeId);
        public partial interface IService : IApplicationService
        {
            [FailureCase("404", Name = "NotFound")]
            [FailureCase<ConflictDetails>("10001", Name = "CanNotDeleteNode")]
            string Delete(Query request);
        }
        """;

    [Fact]
    public void ContractOnlyGenerator_ProducesDistinctTypedCasesAndClosedUnion()
    {
        var output = Generate(Contract, out var run);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var source = run.GeneratedTrees.Single().ToString();
        Assert.Contains("public union DeleteResult(string, global::IService.DeleteResult.NotFound, global::IService.DeleteResult.CanNotDeleteNode)", source);
        Assert.Contains("public global::ConflictDetails Error", source);
        Assert.Contains("public global::NOF.Contract.Error Error", source);
        Assert.Contains("Match<TResult>", source);
        Assert.DoesNotContain("NOF.Domain", source);
        Assert.DoesNotContain("NOF.Abstraction.Failure", source);
    }

    [Theory]
    [InlineData("404")]
    [InlineData("100-01")]
    [InlineData("000404")]
    [InlineData("error/\"quoted\"\ncode")]
    [InlineData("error/code?query=1")]
    [InlineData("错误代码")]
    public void ErrorCodes_AreOpaqueValues_IndependentOfCaseNames(string code)
    {
        var source = $$"""
            using NOF.Contract;
            public record Query;
            public partial interface IService : IApplicationService
            {
                [FailureCase({{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)}})]
                string Run(Query query);
                [FailureCase({{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)}}, Name = "NotFound")]
                string Find(Query query);
            }
            public static class Handler
            {
                public static IService.RunResult Run() => new IService.RunResult.Failed("missing");
                public static IService.FindResult Find() => new IService.FindResult.NotFound("missing");
            }
            """;
        var output = Generate(source, out var run);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = run.GeneratedTrees.Single().ToString();
        Assert.Contains("const string Code = " + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true), generated);
        Assert.Contains("public sealed record Failed", generated);
        Assert.Contains("public sealed record NotFound", generated);
    }

    [Theory]
    [InlineData("[FailureCase(\"\")]", "string", true)]
    [InlineData("[FailureCase(\"A\", Name = \"One\")][FailureCase(\"A\", Name = \"Two\")]", "string", true)]
    [InlineData("[FailureCase(\"A\", Name = \"Match\")]", "string", true)]
    [InlineData("[FailureCase(\"A\", Name = \"Value\")]", "string", true)]
    [InlineData("[FailureCase(\"A\", Name = \"var\")]", "string", true)]
    [InlineData("[FailureCase(\"A\", Name = \"not-valid\")]", "string", true)]
    [InlineData("[FailureCase(\"A\")][FailureCase(\"B\", Name = \"A\")]", "string", true)]
    [InlineData("[FailureCase(\"A\")]", "Result", true)]
    [InlineData("[FailureCase(\"A\")]", "string?", true)]
    [InlineData("[FailureCase(\"A\")]", "string", false)]
    public void InvalidDeclarations_ReportActionableError(string attributes, string response, bool partial)
    {
        var source = $$"""
            #nullable enable
            using NOF.Contract;
            public record Query;
            public {{(partial ? "partial" : "")}} interface IService : IApplicationService
            {
                {{attributes}}
                {{response}} Run(Query request);
            }
            """;
        Generate(source, out var run);
        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NOF220" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UndeclaredFailures_CannotConvertToOperationOutcome()
    {
        var output = Generate(Contract + """

            public static class Handler
            {
                public static IService.DeleteResult Run() => new NOF.Abstraction.Failure("other", "undeclared");
            }
            """, out _);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0029");
    }

    [Fact]
    public void GeneratedJsonClassifier_RoundTripsTypedErrorsAndRejectsUndeclaredCodes()
    {
        var output = Generate(Contract + """

            public static class Probe
            {
                public static string Run()
                {
                    var options = System.Text.Json.JsonSerializerOptions.NOF;
                    IService.DeleteResult failure = new IService.DeleteResult.CanNotDeleteNode(new ConflictDetails(42));
                    var json = System.Text.Json.JsonSerializer.Serialize(failure, options);
                    var restored = System.Text.Json.JsonSerializer.Deserialize<IService.DeleteResult>(json, options);
                    var id = restored.Match(value => -1L, error => -2L, error => error.Error.NodeId);
                    try
                    {
                        System.Text.Json.JsonSerializer.Deserialize<IService.DeleteResult>("{\"$failure\":\"UNDECLARED\",\"error\":{}}", options);
                        return "accepted undeclared error";
                    }
                    catch (System.Text.Json.JsonException) { return id.ToString(); }
                }
            }
            """, out _);
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        Assert.Equal("42", assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("Read();", "NOF230")]
    [InlineData("_ = Read();", "NOF230")]
    [InlineData("ReadAsync();", "NOF230")]
    [InlineData("_ = ReadAsync();", "NOF230")]
    [InlineData("await ReadAsync();", "NOF230")]
    [InlineData("var ignored = Read() switch { string value => value };", "NOF231")]
    [InlineData("var ignored = Read() switch { string value => value, _ => \"ignored\" };", "NOF232")]
    [InlineData("var ignored = Read() switch { object value => value.ToString(), null => \"empty\" };", "NOF232")]
    [InlineData("var ignored = Read() switch { not null => \"ignored\", null => \"empty\" };", "NOF232")]
    [InlineData("switch (Read()) { case string value: break; }", "NOF231")]
    public async Task CheckedOutcomes_RejectIgnoredOrIncompleteHandling(string body, string expected)
    {
        var source = Contract + $$"""

            public static class Consumer
            {
                public static IService.DeleteResult Read() => "ok";
                public static System.Threading.Tasks.Task<IService.DeleteResult> ReadAsync()
                    => System.Threading.Tasks.Task.FromResult(Read());
                public static async System.Threading.Tasks.Task Run() { {{body}} }
            }
            """;
        var output = Generate(source, out _);
        var diagnostics = await output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ExpectedOutcomeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == expected && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ExhaustiveMatch_ConsumesAllCasesWithoutCheckedDiagnostics()
    {
        var output = Generate(Contract + """

            public static class Consumer
            {
                public static string Run(IService.DeleteResult result) => result switch
                {
                    string value => value,
                    IService.DeleteResult.NotFound error => error.Error.Message,
                    IService.DeleteResult.CanNotDeleteNode error => error.Error.NodeId.ToString(),
                    null => throw new System.InvalidOperationException()
                };
            }
            """, out _);
        var diagnostics = await output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ExpectedOutcomeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.Empty(diagnostics);
    }

    private static CSharpCompilation Generate(string source, out GeneratorDriverRunResult run)
    {
        var compilation = CSharpCompilation.CreateCompilation("ExpectedFailures_" + Guid.NewGuid().ToString("N"), source, true,
            typeof(FailureCaseAttribute), typeof(Empty), typeof(NOF.Abstraction.Failure), typeof(JsonUnionAttribute));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new FailureCaseGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        run = driver.GetRunResult();
        return (CSharpCompilation)output;
    }
}
