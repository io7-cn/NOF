using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class ApplicationServiceGeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeUnionCases_AreDescribedWithoutControlFlowAnalysis(bool metadataOnly)
    {
        const string contract = """
            using NOF.Contract;
            using NOF.Abstraction;
            namespace HttpUnion;
            public sealed record Query;
            public sealed record Reply(string Value);
            public union Decision(Reply, Failure);
            public interface IService : IApplicationService { Decision Read(Query input); }
            """;
        var declarations = CSharpCompilation.CreateCompilation("UnionContract", contract, true, typeof(IApplicationService), typeof(Failure));
        var source = "public partial class Service : global::NOF.Application.ApplicationService<global::HttpUnion.IService>;";
        var compilation = metadataOnly
            ? CSharpCompilation.CreateCompilation("UnionApplication", source, true, declarations.CreateMetadataReference())
            : CSharpCompilation.CreateCompilation("UnionApplication", contract + "\n" + source, true, typeof(ApplicationService<>), typeof(ServiceProviderServiceExtensions));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ApplicationServiceGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.Contains("new(typeof(global::HttpUnion.Reply))", generated);
        Assert.Contains("new(typeof(global::NOF.Abstraction.Failure), true)", generated);
    }

    [Fact]
    public void GeneratedOperations_PreservePlainAndNullableResponseTypes_WithSharedModels()
    {
        const string source = """
            #nullable enable
            using NOF.Application;
            using NOF.Contract;
            using NOF.Abstraction;
            namespace App;
            public record Query(string Id);
            public record Dto(string Id);
            public interface IOrders : IApplicationService
            {
                Dto Read(Query request);
                Dto Preview(Query request);
                Dto? Find(Query request);
                int? Count(Query request);
            }
            public partial class Orders : ApplicationService<IOrders>;
            """;
        var result = Generate(source,
            typeof(Context), typeof(ServiceProviderServiceExtensions), typeof(ApplicationService<>), typeof(IApplicationService), typeof(ServiceOperation<,>));
        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var code = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith(".ApplicationService.g.cs", StringComparison.Ordinal)).GetRoot().ToFullString();
        Assert.Contains("class Read : global::NOF.Application.ServiceOperation<global::App.Query, global::App.Dto>", code);
        Assert.Contains("class Preview : global::NOF.Application.ServiceOperation<global::App.Query, global::App.Dto>", code);
        Assert.Contains("class Find : global::NOF.Application.ServiceOperation<global::App.Query, global::App.Dto?>", code);
        Assert.Contains("class Count : global::NOF.Application.ServiceOperation<global::App.Query, int?>", code);
        Assert.Equal(2, code.Split("public override bool AllowsNullResponse => true;").Length - 1);
        Assert.DoesNotContain("IRpcResponse", code);
        Assert.Contains("Task<object?> InvokeAsync(", code);
        Assert.Contains("nameof(global::App.IOrders.Find)", code);
        Assert.DoesNotContain("execution.Require", code);
        Assert.Contains("resultCases:", code);
        Assert.DoesNotContain("ServiceClient", code);
    }

    [Fact]
    public void ApplicationServiceAnalyzer_SupportedDiagnostics_ShouldOnlyContainPartialRule()
    {
        var analyzer = new ApplicationServiceAnalyzer();
        var ids = analyzer.SupportedDiagnostics.Select(d => d.Id).ToArray();

        Assert.Equal(["NOF300"], ids);
    }

    [Fact]
    public void GeneratedCode_SplitsRpcInterfaceIntoPerMethodAbstractClasses()
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            using NOF.Abstraction;

            namespace App
            {
                public record PingRequest(string Value);
                public record GetRequest(int Id);
                public record ArchiveRequest(int Id);
                public record MyDto(string Value);

                public interface IMyService : IApplicationService
                {
                    NOF.Testing.TestOutcome Ping(PingRequest request);
                    NOF.Testing.TestOutcome<MyDto> Get(GetRequest request);
                    NOF.Testing.TestOutcome Archive(ArchiveRequest request);
                }

                public partial class MyService : ApplicationService<IMyService>;
            }
            """;

        var result = Generate(source,
            typeof(Context), typeof(ServiceProviderServiceExtensions), typeof(ApplicationService<>),
            typeof(ServiceOperation<,>),
            typeof(AutoInjectAttribute),
            typeof(Empty),
            typeof(IApplicationService),
            typeof(NOF.Testing.TestOutcome));
        var generatedCode = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith(".ApplicationService.g.cs", StringComparison.Ordinal)).GetRoot().ToFullString();

        Assert.Contains("partial class MyService : global::NOF.Application.IApplicationServiceImplementation", generatedCode);
        Assert.Contains("protected override global::System.Collections.Generic.IReadOnlyDictionary<string, global::NOF.Application.ServiceOperationMapping> GetHandlerMappings()", generatedCode);
        Assert.Contains("public static global::System.Collections.Generic.IReadOnlyDictionary<string, global::NOF.Application.ServiceOperationMapping> HandlerMappings => __rpcHandlerMappings;", generatedCode);
        Assert.Contains("public abstract class Ping : global::NOF.Application.ServiceOperation<global::App.PingRequest, global::NOF.Testing.TestOutcome>", generatedCode);
        Assert.Contains("public abstract class Get : global::NOF.Application.ServiceOperation<global::App.GetRequest, global::NOF.Testing.TestOutcome<global::App.MyDto>>", generatedCode);
        Assert.Contains("public abstract class Archive : global::NOF.Application.ServiceOperation<global::App.ArchiveRequest, global::NOF.Testing.TestOutcome>", generatedCode);
        Assert.Contains("nameof(global::App.IMyService.Ping)] = new global::NOF.Application.ServiceOperationMapping(typeof(Ping), typeof(global::App.PingRequest), typeof(global::NOF.Testing.TestOutcome))", generatedCode);
        Assert.Contains("nameof(global::App.IMyService.Get)] = new global::NOF.Application.ServiceOperationMapping(typeof(Get), typeof(global::App.GetRequest), typeof(global::NOF.Testing.TestOutcome<global::App.MyDto>))", generatedCode);
        Assert.Contains("nameof(global::App.IMyService.Archive)] = new global::NOF.Application.ServiceOperationMapping(typeof(Archive), typeof(global::App.ArchiveRequest), typeof(global::NOF.Testing.TestOutcome))", generatedCode);
        Assert.DoesNotContain("CreateFailure", generatedCode);
    }
    private static GeneratorDriverRunResult Generate(string source, params Type[] references)
    {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.CreateCompilation("ApplicationOperations", source, true, references);
        GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            [new ApplicationServiceGenerator().AsSourceGenerator()],
            parseOptions: (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult();
    }

}
