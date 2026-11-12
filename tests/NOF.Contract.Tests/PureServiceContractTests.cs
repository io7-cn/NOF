using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Abstraction;
using NOF.Contract.SourceGenerator;
using System.Collections.Immutable;
using Xunit;

namespace NOF.Contract.Tests;

public sealed class PureServiceContractTests
{
    [Fact]
    public async Task PlainContract_AllowsConstructedRequestsAndNullableBusinessResponses()
    {
        const string source = """
            #nullable enable
            using NOF.Contract;
            public sealed class Query(long id) { public long Id { get; } = id; }
            public record Dto(long Id);
            public interface IService : IApplicationService
            {
                Dto? Find(Query request);
                Result<Dto> Save(Query request);
            }
            """;
        Assert.Empty(await DiagnoseAsync(source));
    }

    [Theory]
    [InlineData("Task<int>")]
    [InlineData("ValueTask<int>")]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public async Task AsyncContractReturn_IsRejectedWithoutAnHttpAdapter(string returnType)
    {
        var source = $$"""
            using NOF.Contract;
            using System.Threading.Tasks;
            public record Query(long Id);
            public interface IService : IApplicationService { {{returnType}} Read(Query request); }
            """;
        Assert.Contains(await DiagnoseAsync(source), static diagnostic => diagnostic.Id == "NOF207");
    }

    [Fact]
    public void ContractAnalyzer_ShipsOnlyTransportIndependentDiagnostics()
        => Assert.Equal(["NOF200", "NOF207", "NOF208", "NOF209"],
            new ApplicationServiceAnalyzer().SupportedDiagnostics.Select(static diagnostic => diagnostic.Id));

    private static Task<ImmutableArray<Diagnostic>> DiagnoseAsync(string source)
    {
        var compilation = CSharpCompilation.CreateCompilation("PureContract", source, true,
            typeof(IApplicationService).ToMetadataReference(), typeof(Failure).ToMetadataReference());
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ApplicationServiceAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
