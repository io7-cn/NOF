using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NOF.Contract.SourceGenerator;
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
            [FailureCase("NotFound", "404")]
            [FailureCase("CanNotDeleteNode", typeof(ConflictDetails))]
            string Delete(Query request);
        }
        """;

    [Fact]
    public void ContractOnlyGenerator_ProducesDistinctTypedCasesAndClosedUnion()
    {
        var output = Generate(Contract, out var run);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var source = run.GeneratedTrees.Single().ToString();
        Assert.Contains("public union Result(string, global::IService.DeleteOperation.NotFound, global::IService.DeleteOperation.CanNotDeleteNode)", source);
        Assert.Contains("public global::ConflictDetails Error", source);
        Assert.Contains("public string ErrorCode", source);
        Assert.Contains("public string Message", source);
        Assert.Contains("Match<TResult>", source);
        Assert.DoesNotContain("NOF.Domain", source);
        Assert.DoesNotContain("NOF.Application", source);
        Assert.DoesNotContain("NOF.Abstraction.Failure", source);
    }

    [Fact]
    public void ContractOperationBase_ExposesInheritedResultAndCaseTypes()
    {
        var output = Generate(Contract + """

            public sealed class Handler : IService.DeleteOperation
            {
                public override System.Threading.Tasks.Task<Result> HandleAsync(
                    Query request, Context context, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult<Result>(new NotFound("missing"));
            }
            """, out _);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(typeof(IApplicationService).Assembly, typeof(ServiceOperation).Assembly);
        Assert.DoesNotContain(typeof(ServiceOperation).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "NOF.Application");
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
                [FailureCase("Failed", {{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)}})]
                string Run(Query query);
                [FailureCase("NotFound", {{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)}})]
                string Find(Query query);
            }
            public static class Handler
            {
                public static IService.RunOperation.Result Run() => new IService.RunOperation.Failed("missing");
                public static IService.FindOperation.Result Find() => new IService.FindOperation.NotFound("missing");
            }
            """;
        var output = Generate(source, out var run);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = run.GeneratedTrees.Single().ToString();
        Assert.Contains("get => " + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true), generated);
        Assert.Contains("public sealed record Failed", generated);
        Assert.Contains("public sealed record NotFound", generated);
    }

    [Theory]
    [InlineData("[FailureCase(\"\")]", "string", true)]
    [InlineData("[FailureCase(\"Same\")][FailureCase(\"Same\", \"B\")]", "string", true)]
    [InlineData("[FailureCase(\"Match\")]", "string", true)]
    [InlineData("[FailureCase(\"Value\")]", "string", true)]
    [InlineData("[FailureCase(\"Descriptor\")]", "string", true)]
    [InlineData("[FailureCase(\"Result\")]", "string", true)]
    [InlineData("[FailureCase(\"var\")]", "string", true)]
    [InlineData("[FailureCase(\"not-valid\")]", "string", true)]
    [InlineData("[FailureCase(\"404\")]", "string", true)]
    [InlineData("[FailureCase(\"class\")]", "string", true)]
    [InlineData("[FailureCase(\"NotFound\", \" \")]", "string", true)]
    [InlineData("[FailureCase(\"Custom\", typeof(void))]", "string", true)]
    [InlineData("[FailureCase(\"Custom\", typeof(System.Collections.Generic.List<>))]", "string", true)]
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
                public static IService.DeleteOperation.Result Run() => new NOF.Abstraction.Failure("other", "undeclared");
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
                    IService.DeleteOperation.Result failure = new IService.DeleteOperation.CanNotDeleteNode(new ConflictDetails(42));
                    var json = System.Text.Json.JsonSerializer.Serialize(failure, options);
                    var restored = System.Text.Json.JsonSerializer.Deserialize<IService.DeleteOperation.Result>(json, options);
                    var id = restored.Match(value => -1L, error => -2L, error => error.Error.NodeId);
                    try
                    {
                        System.Text.Json.JsonSerializer.Deserialize<IService.DeleteOperation.Result>("{\"$failure\":\"UNDECLARED\",\"error\":{}}", options);
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

    [Fact]
    public void NamedFailures_DefaultCodeAndCustomDataRoundTripWithoutSharedErrorWrapper()
    {
        var output = Generate("""
            using NOF.Contract;
            using System.Text.Json;
            public sealed record Query;
            public readonly record struct Details(int Count);
            public partial interface IService : IApplicationService
            {
                [FailureCase("Missing")]
                [FailureCase("Denied", "404")]
                [FailureCase("Hidden", "404")]
                [FailureCase("Conflict", typeof(Details))]
                string Run(Query request);
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var options = System.Text.Json.JsonSerializerOptions.NOF;
                    IService.RunOperation.Result missing = new IService.RunOperation.Missing("missing");
                    var json = System.Text.Json.JsonSerializer.Serialize(missing, options);
                    var restored = System.Text.Json.JsonSerializer.Deserialize<IService.RunOperation.Result>(json, options);
                    IService.RunOperation.Result custom = new IService.RunOperation.Conflict(new Details(42));
                    var customJson = System.Text.Json.JsonSerializer.Serialize(custom, options);
                    var restoredCustom = System.Text.Json.JsonSerializer.Deserialize<IService.RunOperation.Result>(customJson, options);
                    IService.RunOperation.Result hidden = new IService.RunOperation.Hidden("hidden");
                    var restoredHidden = System.Text.Json.JsonSerializer.Deserialize<IService.RunOperation.Result>(
                        System.Text.Json.JsonSerializer.Serialize(hidden, options), options);
                    return restored is IService.RunOperation.Missing { ErrorCode: "Missing", Message: "missing" }
                        && new IService.RunOperation.Denied().ErrorCode == "404"
                        && restoredHidden is IService.RunOperation.Hidden { ErrorCode: "404" }
                        && restoredCustom is IService.RunOperation.Conflict { Error.Count: 42 }
                        && typeof(IService.RunOperation.Conflict).GetProperty("ErrorCode") == null
                        && typeof(IService.RunOperation.Missing).GetProperty("Error") == null
                        && !customJson.Contains("errorCode");
                }
            }
            """, out _);
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        Assert.True((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void NativeUnionCompiler_WarnsWhenSwitchExpressionOmitsFailureCases()
    {
        var output = Generate(Contract + """

            public static class Consumer
            {
                public static string Run(IService.DeleteOperation.Result result) => result switch
                {
                    string value => value,
                    null => throw new System.InvalidOperationException()
                };
            }
            """, out _);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS8509");
    }

    [Fact]
    public void NativeUnionCompiler_AcceptsExhaustiveSwitchAndOrdinaryDiscardSemantics()
    {
        var output = Generate(Contract + """

            public static class Consumer
            {
                public static IService.DeleteOperation.Result Read() => "ok";
                public static void Ignore() { Read(); _ = Read(); }
                public static string Run(IService.DeleteOperation.Result result) => result switch
                {
                    string value => value,
                    IService.DeleteOperation.NotFound error => error.Message,
                    IService.DeleteOperation.CanNotDeleteNode error => error.Error.NodeId.ToString(),
                    null => throw new System.InvalidOperationException()
                };
            }
            """, out _);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            || diagnostic.Id is "CS8509" or "CS8655");
    }

    [Fact]
    public void ImplementationFailureDeclarations_ReportDeclarationError()
    {
        Generate("""
            using NOF.Contract;
            public sealed class Handler
            {
                [FailureCase("Missing")]
                public string Run(object request) => "ok";
            }
            """, out var run);
        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "NOF220");
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
