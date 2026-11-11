using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class ApplicationServiceGeneratorTests
{
    [Fact]
    public void GeneratedOperations_PreservePlainAndNullableResponseTypes_WithSharedModels()
    {
        const string source = """
            #nullable enable
            using NOF.Application;
            using NOF.Contract;
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
        var result = new ApplicationServiceGenerator().GetResultPostGen(source,
            typeof(ApplicationService<>), typeof(IApplicationService), typeof(ServiceOperation<,>));
        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var code = result.GeneratedTrees.Single().GetRoot().ToFullString();
        Assert.Contains("class Read : global::NOF.Application.ServiceOperation<global::App.Query, global::App.Dto>", code);
        Assert.Contains("class Preview : global::NOF.Application.ServiceOperation<global::App.Query, global::App.Dto>", code);
        Assert.Contains("class Find : global::NOF.Application.ServiceOperation<global::App.Query, global::App.Dto?>", code);
        Assert.Contains("class Count : global::NOF.Application.ServiceOperation<global::App.Query, int?>", code);
        Assert.Equal(2, code.Split("public override bool AllowsNullResponse => true;").Length - 1);
        Assert.DoesNotContain("IRpcResponse", code);
        Assert.Contains("Task<global::App.Dto?> InvokeAsync(", code);
        Assert.Contains("nameof(global::App.IOrders.Find)", code);
        Assert.Contains("execution.Require<global::App.Dto?>(allowNull: true)", code);
        Assert.Contains("execution.Require<global::App.Dto>(allowNull: false)", code);
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

            namespace App
            {
                public record PingRequest(string Value);
                public record GetRequest(int Id);
                public record ArchiveRequest(int Id);
                public record MyDto(string Value);

                public partial interface IMyService : IApplicationService
                {
                    Result Ping(PingRequest request);
                    Result<MyDto> Get(GetRequest request);
                    Result Archive(ArchiveRequest request);
                }

                public partial class MyService : ApplicationService<IMyService>;
            }
            """;

        var result = new ApplicationServiceGenerator().GetResultPostGen(source,
            typeof(ApplicationService<>),
            typeof(ServiceOperation<,>),
            typeof(AutoInjectAttribute),
            typeof(Empty),
            typeof(IApplicationService),
            typeof(Result));
        var generatedCode = result.GeneratedTrees.Single().GetRoot().ToFullString();

        Assert.Contains("partial class MyService : global::NOF.Application.IApplicationServiceImplementation", generatedCode);
        Assert.Contains("protected override global::System.Collections.Generic.IReadOnlyDictionary<string, global::NOF.Application.ServiceOperationMapping> GetHandlerMappings()", generatedCode);
        Assert.Contains("public static global::System.Collections.Generic.IReadOnlyDictionary<string, global::NOF.Application.ServiceOperationMapping> HandlerMappings => __rpcHandlerMappings;", generatedCode);
        Assert.Contains("public abstract class Ping : global::NOF.Application.ServiceOperation<global::App.PingRequest, global::NOF.Contract.Result>", generatedCode);
        Assert.Contains("public abstract class Get : global::NOF.Application.ServiceOperation<global::App.GetRequest, global::NOF.Contract.Result<global::App.MyDto>>", generatedCode);
        Assert.Contains("public abstract class Archive : global::NOF.Application.ServiceOperation<global::App.ArchiveRequest, global::NOF.Contract.Result>", generatedCode);
        Assert.Contains("nameof(global::App.IMyService.Ping)] = new global::NOF.Application.ServiceOperationMapping(typeof(Ping), typeof(global::App.PingRequest), typeof(global::NOF.Contract.Result))", generatedCode);
        Assert.Contains("nameof(global::App.IMyService.Get)] = new global::NOF.Application.ServiceOperationMapping(typeof(Get), typeof(global::App.GetRequest), typeof(global::NOF.Contract.Result<global::App.MyDto>))", generatedCode);
        Assert.Contains("nameof(global::App.IMyService.Archive)] = new global::NOF.Application.ServiceOperationMapping(typeof(Archive), typeof(global::App.ArchiveRequest), typeof(global::NOF.Contract.Result))", generatedCode);
        Assert.Contains("static failure => global::NOF.Contract.Result<global::App.MyDto>.CreateFailure(failure)", generatedCode);
    }
}
