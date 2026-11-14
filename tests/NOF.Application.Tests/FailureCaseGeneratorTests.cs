using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using System.Reflection;
using System.Text.Json.Serialization;
using Xunit;

namespace NOF.Application.Tests;

public sealed class FailureCaseGeneratorTests
{
    private const string Contract = """
        using NOF.Contract;
        using System.Text.Json;
        using NOF.Application;
        public sealed record Query(long Id);
        public sealed record ConflictDetails(long NodeId);
        public interface IService : IApplicationService
        {
            [FailureCase("NotFound", "404")]
            [FailureCase("CanNotDeleteNode", "CanNotDeleteNode", typeof(ConflictDetails))]
            string Delete(Query request);
        }
        """;

    [Fact]
    public void ApplicationGenerator_ProducesDistinctTypedCasesAndClosedUnion()
    {
        var output = Generate(Contract, out var run);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var source = run.GeneratedTrees.Single().ToString();
        Assert.Contains("public union Result(string, global::Service.Delete.NotFound, global::Service.Delete.CanNotDeleteNode)", source);
        Assert.Contains("public global::ConflictDetails Error", source);
        Assert.Contains("public string Title", source);
        Assert.Contains("public string Detail", source);
        Assert.Contains("Match<TResult>", source);
        Assert.DoesNotContain("NOF.Domain", source);
        Assert.Contains("NOF.Application", source);
        Assert.DoesNotContain("partial interface", source);
        Assert.DoesNotContain("NOF.Abstraction.Failure", source);
    }

    [Fact]
    public void ApplicationOperation_ExposesResultAndCaseTypes()
    {
        var output = Generate(Contract + """

            public sealed class Handler : Service.Delete
            {
                public override System.Threading.Tasks.Task<Result> HandleAsync(
                    Query request, InvocationContext context, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult<Result>(new NotFound("missing"));
            }
            """, out _);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(typeof(ApplicationService).Assembly, typeof(ServiceOperation).Assembly);
        Assert.DoesNotContain(typeof(IApplicationService).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "NOF.Application");
    }

    [Theory]
    [InlineData("404")]
    [InlineData("100-01")]
    [InlineData("000404")]
    [InlineData("error/\"quoted\"\ncode")]
    [InlineData("error/code?query=1")]
    [InlineData("错误代码")]
    public void Titles_AreOpaqueValues_IndependentOfCaseNames(string code)
    {
        var source = $$"""
            using NOF.Contract;
            using NOF.Application;
            public record Query;
            public interface IService : IApplicationService
            {
                [FailureCase("Failed", {{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)}})]
                string Run(Query query);
                [FailureCase("NotFound", {{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true)}})]
                string Find(Query query);
            }
            public static class Handler
            {
                public static Service.Run.Result Run() => new Service.Run.Failed("missing");
                public static Service.Find.Result Find() => new Service.Find.NotFound("missing");
            }
            """;
        var output = Generate(source, out var run);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = run.GeneratedTrees.Single().ToString();
        Assert.Contains("get => " + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(code, true), generated);
        Assert.Contains("public sealed record Failed", generated);
        Assert.Contains("public sealed record NotFound", generated);
    }

    [Fact]
    public void UndeclaredFailures_CannotConvertToOperationOutcome()
    {
        var output = Generate(Contract + """

            public static class Handler
            {
                public static Service.Delete.Result Run() => new NOF.Abstraction.Failure("other", "undeclared");
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
                    Service.Delete.Result failure = new Service.Delete.CanNotDeleteNode(new ConflictDetails(42));
                    var json = System.Text.Json.JsonSerializer.Serialize(failure, options);
                    var restored = System.Text.Json.JsonSerializer.Deserialize<Service.Delete.Result>(json, options);
                    var id = restored.Match(value => -1L, error => -2L, error => error.Error.NodeId);
                    try
                    {
                        System.Text.Json.JsonSerializer.Deserialize<Service.Delete.Result>("{\"$failure\":\"UNDECLARED\",\"error\":{}}", options);
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
            using NOF.Application;
            public sealed record Query;
            public readonly record struct Details(int Count);
            public interface IService : IApplicationService
            {
                [FailureCase("Missing")]
                [FailureCase("Denied", "404")]
                [FailureCase("Hidden", "404")]
                [FailureCase("Conflict", "Conflict", typeof(Details))]
                string Run(Query request);
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var options = System.Text.Json.JsonSerializerOptions.NOF;
                    Service.Run.Result missing = new Service.Run.Missing("missing");
                    var json = System.Text.Json.JsonSerializer.Serialize(missing, options);
                    var restored = System.Text.Json.JsonSerializer.Deserialize<Service.Run.Result>(json, options);
                    Service.Run.Result custom = new Service.Run.Conflict(new Details(42));
                    var customJson = System.Text.Json.JsonSerializer.Serialize(custom, options);
                    var restoredCustom = System.Text.Json.JsonSerializer.Deserialize<Service.Run.Result>(customJson, options);
                    Service.Run.Result hidden = new Service.Run.Hidden("hidden");
                    var restoredHidden = System.Text.Json.JsonSerializer.Deserialize<Service.Run.Result>(
                        System.Text.Json.JsonSerializer.Serialize(hidden, options), options);
                    return restored is Service.Run.Missing { Title: "Missing", Detail: "missing" }
                        && new Service.Run.Denied().Title == "404"
                        && restoredHidden is Service.Run.Hidden { Title: "404" }
                        && restoredCustom is Service.Run.Conflict { Error.Count: 42 }
                        && typeof(Service.Run.Conflict).GetProperty("Title") != null
                        && typeof(Service.Run.Missing).GetProperty("Error") == null
                        && customJson.Contains("title");
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
                public static string Run(Service.Delete.Result result) => result switch
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
                public static Service.Delete.Result Read() => "ok";
                public static void Ignore() { Read(); _ = Read(); }
                public static string Run(Service.Delete.Result result) => result switch
                {
                    string value => value,
                    Service.Delete.NotFound error => error.Detail,
                    Service.Delete.CanNotDeleteNode error => error.Error.NodeId.ToString(),
                    null => throw new System.InvalidOperationException()
                };
            }
            """, out _);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            || diagnostic.Id is "CS8509" or "CS8655");
    }

    private static CSharpCompilation Generate(string source, out GeneratorDriverRunResult run)
    {
        source += "\npublic partial class Service : global::NOF.Application.ApplicationService<IService>;";
        var compilation = CSharpCompilation.CreateCompilation("ExpectedFailures_" + Guid.NewGuid().ToString("N"), source, true,
            typeof(ApplicationService<>), typeof(ServiceProviderServiceExtensions), typeof(FailureCaseAttribute), typeof(Empty), typeof(Abstraction.Failure), typeof(JsonUnionAttribute));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ApplicationServiceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        run = driver.GetRunResult();
        return (CSharpCompilation)output;
    }
}
