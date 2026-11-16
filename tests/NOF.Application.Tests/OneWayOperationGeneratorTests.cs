using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using Xunit;

namespace NOF.Application.Tests;

public sealed class OneWayOperationGeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VoidAndEmpty_GenerateDistinctHandlerAndInvocationSurfaces(bool separateContract)
    {
        const string Contract = """
            using NOF.Contract;
            namespace OneWay;
            public sealed record Message;
            public interface IService : IApplicationService
            {
                void Send(Message request);
                [FailureCase("Denied")]
                Empty Reply(Message request);
            }
            """;
        const string Application = """
            using System.Threading;
            using System.Threading.Tasks;
            using NOF.Application;
            public partial class Server : ApplicationService<OneWay.IService>;
            public sealed class Send : Server.Send
            {
                public override Task HandleAsync(OneWay.Message request, InvocationContext context, CancellationToken ct)
                    => Task.CompletedTask;
            }
            public sealed class Reply : Server.Reply
            {
                public override Task<Result> HandleAsync(OneWay.Message request, InvocationContext context, CancellationToken ct)
                    => Task.FromResult<Result>(new Denied());
            }
            """;
        var declarations = CSharpCompilation.CreateCompilation("OneWayContract", Contract, true, typeof(IApplicationService));
        var compilation = CSharpCompilation.CreateCompilation("OneWayApplication", Application, true,
            typeof(ApplicationService<>), typeof(Context), typeof(ServiceProviderServiceExtensions),
            typeof(System.Text.Json.Serialization.JsonUnionAttribute));
        compilation = separateContract ? compilation.AddReferences(declarations.CreateMetadataReference())
            : compilation.AddSyntaxTrees(declarations.SyntaxTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ApplicationServiceGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics.Concat(output.GetDiagnostics()), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var send = output.GetTypeByMetadataName("Server+Send")!;
        Assert.Equal("NOF.Application.OperationHandler<OneWay.Message>", send.BaseType!.ToDisplayString());
        Assert.Empty(send.GetTypeMembers("Result"));
        Assert.Empty(send.GetMembers("InvokeAsync"));
        Assert.Equal("NOF.Application.OperationDescriptor<OneWay.Message>", ((IPropertySymbol)send.GetMembers("Descriptor").Single()).Type.ToDisplayString());
        var reply = output.GetTypeByMetadataName("Server+Reply")!;
        Assert.Single(reply.GetTypeMembers("Result"));
        Assert.Single(reply.GetTypeMembers("Denied"));
        Assert.Single(reply.GetMembers("InvokeAsync"));
    }
}
