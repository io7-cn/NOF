using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Contract;
using NOF.Contract.SourceGenerator;
using System.Collections.Immutable;
using Xunit;
using HttpRpcStyle = NOF.Contract.HttpRpcStyle;
using HttpVerb = NOF.Contract.HttpVerb;

namespace NOF.SourceGenerator.Tests;

public class ApplicationServiceAnalyzerTests
{
    private static readonly Type[] _refs =
    [
        typeof(HttpEndpointAttribute),
        typeof(Empty),
        typeof(IApplicationService),
        typeof(HttpVerb),
        typeof(Result),
        typeof(Result<>),
        typeof(IParsable<>),
        typeof(TransportOverAttribute),
        typeof(TransportOverHttpAttribute),
        typeof(TransportOverMemoryAttribute)
    ];

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var extraReferences = _refs.Select(t => t.ToMetadataReference()).ToArray();
        var compilation = CSharpCompilation.CreateCompilation("TestAssembly", source, true, extraReferences);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ApplicationServiceAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public void TransportOverHttp_ShouldExposeSelectedRpcStyle()
    {
        var attribute = new TransportOverHttpAttribute(HttpRpcStyle.JsonRpc, "/rpc");
        var attributeWithoutPrefix = new TransportOverHttpAttribute(HttpRpcStyle.ControllerRpc);

        Assert.True(typeof(TransportOverAttribute).IsAbstract);
        Assert.Equal(HttpRpcStyle.JsonRpc, attribute.Style);
        Assert.Equal("/rpc", attribute.RoutePrefix);
        Assert.Null(attributeWithoutPrefix.RoutePrefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://example.test/rpc")]
    [InlineData("//rpc")]
    [InlineData("/rpc//v1")]
    [InlineData("/rpc/{tenant}")]
    [InlineData("/rpc?version=1")]
    [InlineData("/rpc#fragment")]
    [InlineData("../rpc")]
    [InlineData("./rpc")]
    [InlineData("/rpc\\v1")]
    [InlineData("/rpc/%76%31")]
    public void TransportOverHttp_WithInvalidRoutePrefix_ShouldThrow(string routePrefix)
    {
        Assert.Throws<ArgumentException>(() =>
            new TransportOverHttpAttribute(HttpRpcStyle.JsonRpc, routePrefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://example.test/rpc")]
    [InlineData("//rpc")]
    [InlineData("/rpc//v1")]
    [InlineData("/rpc/{tenant}")]
    [InlineData("/rpc?version=1")]
    [InlineData("/rpc#fragment")]
    [InlineData("../rpc")]
    [InlineData("./rpc")]
    [InlineData("/rpc\\v1")]
    [InlineData("/rpc/%76%31")]
    public async Task TransportOverHttp_WithInvalidRoutePrefix_ShouldReportNOF215(string routePrefix)
    {
        var routePrefixLiteral = SymbolDisplay.FormatLiteral(routePrefix, quote: true);
        var source = $$"""
            using NOF.Contract;

            namespace App;

            [TransportOverHttp(HttpRpcStyle.JsonRpc, {{routePrefixLiteral}})]
            public interface IMyService : IApplicationService
            {
                Result Ping(Empty request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Single(diagnostics, static diagnostic => diagnostic.Id == "NOF215");
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("/rpc")]
    [InlineData("rpc/")]
    [InlineData("/")]
    [InlineData("/api/v1")]
    public async Task TransportOverHttp_WithValidRoutePrefix_ShouldNotReportNOF215(string routePrefix)
    {
        var routePrefixLiteral = SymbolDisplay.FormatLiteral(routePrefix, quote: true);
        var source = $$"""
            using NOF.Contract;

            namespace App;

            [TransportOverHttp(HttpRpcStyle.JsonRpc, {{routePrefixLiteral}})]
            public interface IMyService : IApplicationService
            {
                Result Ping(Empty request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "NOF215");
    }

    [Fact]
    public async Task TransportOverHttp_OnRpcServiceContract_ShouldBeValid()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            [TransportOverHttp(HttpRpcStyle.ControllerRpc, "/api")]
            public interface IMyService : IApplicationService
            {
                Result Ping(Empty request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id is "NOF211" or "NOF212" or "NOF214");
    }

    [Fact]
    public async Task TransportOverMemory_OnRpcServiceContract_ShouldBeValid()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            [TransportOverMemory]
            public interface IMyService : IApplicationService
            {
                Result Ping(Empty request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id is "NOF211" or "NOF212" or "NOF214");
    }

    [Fact]
    public async Task TransportOverHttp_OnNonRpcServiceInterface_ShouldReportNOF211()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            [TransportOverHttp(HttpRpcStyle.JsonRpc)]
            public interface INotAnRpcService;
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Single(diagnostics, static diagnostic => diagnostic.Id == "NOF211");
    }

    [Fact]
    public async Task RpcServiceContract_WithMultipleTransportAttributes_ShouldReportNOF212()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            [TransportOverHttp(HttpRpcStyle.JsonRpc)]
            [TransportOverMemory]
            public interface IMyService : IApplicationService
            {
                Result Ping(Empty request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Single(diagnostics, static diagnostic => diagnostic.Id == "NOF212");
    }

    [Fact]
    public async Task ApplicationServiceContract_WithoutTransportAttribute_IsValid()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public interface IMyService : IApplicationService
            {
                Result Ping(Empty request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task JsonRpcServiceMethod_WithHttpEndpoint_ShouldReportNOF213()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record PingRequest(string Value);

            [TransportOverHttp(HttpRpcStyle.JsonRpc, "/rpc")]
            public interface IMyService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Post, "/ping")]
                Result Ping(PingRequest request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics, static diagnostic => diagnostic.Id == "NOF213");
        Assert.Contains(nameof(HttpEndpointAttribute), diagnostic.GetMessage());
    }

    [Fact]
    public async Task ControllerRpcServiceMethod_WithHttpEndpoint_ShouldNotReportNOF213()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record PingRequest(string Value);

            [TransportOverHttp(HttpRpcStyle.ControllerRpc, "/api")]
            public interface IMyService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Post, "/ping")]
                Result Ping(PingRequest request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "NOF213");
    }

    [Fact]
    public async Task MemoryRpcServiceMethod_WithHttpEndpoint_ShouldReportNOF216()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record PingRequest(string Value);

            [TransportOverMemory]
            public interface IMyService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Post, "/ping")]
                Result Ping(PingRequest request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics, static diagnostic => diagnostic.Id == "NOF216");
        Assert.Contains(nameof(HttpEndpointAttribute), diagnostic.GetMessage());
    }

    [Fact]
    public async Task StructRequest_WithHttpEndpoint_ReportsNOF200()
    {
        const string source = """
            using NOF.Contract;
            namespace App
            {
                [HttpEndpoint(HttpVerb.Post, "/api/items")]
                public struct CreateItemRequest
                {
                    public string Name { get; set; }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Contains(diagnostics, d => d.Id == "NOF200");
    }

    [Fact]
    public async Task RouteParametersNotSupported_ReportsNOF201()
    {
        const string source = """
            using NOF.Contract;
            namespace App
            {
                [HttpEndpoint(HttpVerb.Put, "/api/items/{id}")]
                public class UpdateItemRequest
                {
                    public string Name { get; set; } = default!;
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF201");
    }

    [Theory]
    [InlineData("/items/{id}", "Guid", "Id", null)]
    [InlineData("/items/{id}", "Guid", "OrderId", "publicly readable and writable request property")]
    [InlineData("/items/{id}", "string", "Id", null)]
    [InlineData("/items/{id}", "DayOfWeek", "Id", null)]
    [InlineData("/items/{id}", "DateOnly", "Id", null)]
    [InlineData("/items/{id}", "Guid?", "Id", "compatible public static TryParse")]
    [InlineData("/items/{id}", "IncompatibleParser", "Id", "compatible public static TryParse")]
    [InlineData("/items/{id?}", "Guid", "Id", "unique, required parameter name")]
    [InlineData("/items/{id}/{ID}", "Guid", "Id", "unique, required parameter name")]
    public async Task ControllerRoute_ValidatesPathBindings(string route, string type, string property, string? error)
    {
        var source = $$"""
            using System;
            using NOF.Contract;

            public sealed class UpdateItemRequest
            {
                public {{type}} {{property}} { get; init; }
                public string Name { get; set; } = "";
            }

            public struct IncompatibleParser
            {
                public static bool TryParse(string text, int style, out IncompatibleParser result)
                {
                    result = default;
                    return true;
                }
            }

            [TransportOverHttp(HttpRpcStyle.ControllerRpc)]
            public interface IItemService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Put, "{{route}}")]
                Result Update(UpdateItemRequest request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        if (error is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics, item => item.Id == "NOF217");
            Assert.Contains(error, diagnostic.GetMessage());
        }
    }

    [Fact]
    public async Task ClassWithoutParameterlessCtor_ReportsNOF202()
    {
        const string source = """
            using NOF.Contract;
            namespace App
            {
                [HttpEndpoint(HttpVerb.Post, "/api/items")]
                public class CreateItemRequest
                {
                    public CreateItemRequest(string name) { Name = name; }
                    public string Name { get; set; }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Contains(diagnostics, d => d.Id == "NOF202");
    }

    [Fact]
    public async Task GetEndpoint_WithUnparsableQueryProperty_ReportsNOF203()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public sealed class QueryRequest
            {
                public ComplexValue Value { get; set; } = new();
            }

            public sealed class ComplexValue;

            public partial interface IMyService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Get, "/api/items")]
                Result Get(QueryRequest request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF203");
    }

    [Fact]
    public async Task GetEndpoint_WithParsableProperty_NoNOF203()
    {
        const string source = """
            using NOF.Contract;
            using System;

            namespace App;

            public sealed class CommandRequest
            {
                public StrongValue Value { get; set; }
            }

            public readonly record struct StrongValue(string Value) : IParsable<StrongValue>
            {
                public static StrongValue Parse(string value, IFormatProvider? provider)
                    => new(value);

                public static bool TryParse(string? value, IFormatProvider? provider, out StrongValue result)
                {
                    result = new StrongValue(value ?? string.Empty);
                    return true;
                }
            }

            public partial interface IMyService : IApplicationService
            {
                [HttpEndpoint(HttpVerb.Get, "/api/items")]
                Result Execute(CommandRequest request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NOF203");
    }

    [Fact]
    public async Task ServiceMethod_WithTwoBusinessParameters_ReportsNOF207()
    {
        const string source = """
            using NOF.Contract;
            using System.Threading.Tasks;

            namespace App;

            public record Query1(string Value);
            public record Query2(string Value);

            
            public partial interface IMyService : IApplicationService
            {
                Task<Result> DoAsync(Query1 first, Query2 second);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF207");
    }

    [Fact]
    public async Task ServiceMethod_WithoutRequestParameter_ReportsNOF207()
    {
        const string source = """
            using NOF.Contract;
            using System.Threading.Tasks;

            namespace App;

            
            public partial interface IMyService : IApplicationService
            {
                Task<Result> DoAsync();
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF207");
    }

    [Fact]
    public async Task ServiceMethod_WithCancellationToken_ReportsNOF207()
    {
        const string source = """
            using NOF.Contract;
            using System.Threading;
            using System.Threading.Tasks;

            namespace App;

            public record Query(string Value);

            
            public partial interface IMyService : IApplicationService
            {
                Task<Result> DoAsync(Query request, CancellationToken cancellationToken = default);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF207");
    }

    [Fact]
    public async Task ServiceMethod_WithSyncReturnAndSingleRequest_NoNOF207()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record Query(string Value);

            
            public partial interface IMyService : IApplicationService
            {
                Result Do(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NOF207");
    }

    [Fact]
    public async Task ServiceMethod_WithPlainDtoReturn_IsValid()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record Query(string Value);
            public record MyResponse(string Value);

            public partial interface IMyService : IApplicationService
            {
                MyResponse Get(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ServiceMethod_WithResultOfEmptyReturn_NoSignatureDiagnostics()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record Query(string Value);

            public partial interface IMyService : IApplicationService
            {
                Result<Empty> Execute(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id is "NOF207" or "NOF209");
    }

    [Fact]
    public async Task ServiceMethod_WithCustomResultImplementation_NoNOF210()
    {
        const string source = """
            using NOF.Abstraction;
            using NOF.Contract;
            using System.Collections.Generic;

            namespace App;

            public record Query(string Value);

            public union CustomResult(Success, Failure) : IRpcResponse<CustomResult>
            {
                public static CustomResult CreateFailure(Failure failure) => failure;
                public static CustomResult CreateEmptyResponse() => new Success();
            }

            public partial interface IMyService : IApplicationService
            {
                CustomResult Execute(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "NOF210");
    }

    [Fact]
    public async Task ServiceMethod_WithVoidReturn_ReportsNOF209()
    {
        const string source = """
            using NOF.Contract;

            namespace App;

            public record Query(string Value);

            public partial interface IMyService : IApplicationService
            {
                void Execute(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Contains(diagnostics, d => d.Id == "NOF209");
    }

    [Fact]
    public async Task ServiceMethod_WithValueTaskReturn_ReportsNOF207()
    {
        const string source = """
            using NOF.Contract;
            using System.Threading.Tasks;

            namespace App;

            public record Query(string Value);

            
            public partial interface IMyService : IApplicationService
            {
                ValueTask<Result> DoAsync(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF207");
    }

    [Fact]
    public async Task ServiceMethod_WithTaskAndSingleRequest_ReportsNOF207()
    {
        const string source = """
            using NOF.Contract;
            using System.Threading.Tasks;

            namespace App;

            public record Query(string Value);

            
            public partial interface IMyService : IApplicationService
            {
                Task<Result<string>> DoAsync(Query request);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Single(diagnostics, d => d.Id == "NOF207");
    }
}
