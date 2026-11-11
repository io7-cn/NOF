using Microsoft.CodeAnalysis;
using NOF.Contract.SourceGenerator;
using Xunit;

namespace NOF.Contract.Tests;

public class HttpServiceEndpointGeneratorTests
{
    [Fact]
    public void GeneratesOnlyCompilableServerMetadata()
    {
        var source = $$"""
            using NOF.Contract;
            namespace App;
            public sealed class UpdateRequest
            {
                public required long Id { get; init; }
                public required string Name { get; init; }
            }
            [TransportOverHttp(HttpRpcStyle.ControllerRpc, "/nodes")]
            public interface INodeService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Put, "/{id}")]
                Result Update(UpdateRequest request);
            }
            """;
        var result = new HttpServiceEndpointGenerator().GetResultPostGen(source,
            typeof(IApplicationService), typeof(GeneratedHttpRpcRoute), typeof(Result));
        var code = result.GeneratedTrees.Single().GetRoot().ToFullString();
        Assert.Contains("GeneratedHttpRpcRouteAttribute<", code);
        Assert.Contains("BindAsync(", code);
        Assert.Contains("JsonMetadataServices.CreateObjectInfo", code);
        Assert.DoesNotContain("HttpClient", code);
        Assert.DoesNotContain("ServiceClient", code);
        Assert.DoesNotContain("CreateBody(", code);
        Assert.DoesNotContain("SendAsync(", code);
    }

    [Fact]
    public void UndecoratedProtoContract_GeneratesNoHttpOrClientCode()
    {
        const string source = """
            using NOF.Contract;
            namespace App;
            public record Query(long Id);
            public record Dto(long Id);
            public interface IService : IApplicationService { Dto Get(Query request); }
            """;
        var result = new HttpServiceEndpointGenerator().GetResultPostGen(source, typeof(IApplicationService));
        Assert.Empty(result.GeneratedTrees);
    }
}
