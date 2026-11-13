using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using System.Reflection;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public sealed class InheritedFailureCaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InheritedTypes_ShouldPreserveContractIdentity(bool reverseOrder)
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed record Query(int Mode);
            public sealed record Details(int Count);
            public partial interface IService : IApplicationService
            {
                [FailureCase("NotFound", "404")]
                [FailureCase("Conflict", typeof(Details))]
                string Run(Query request);
            }
            public partial class Service : ApplicationService<IService>;
            public sealed class Handler : Service.Run
            {
                public override Task<Result> HandleAsync(Query request, InvocationContext context, CancellationToken ct)
                    => Task.FromResult<Result>(request.Mode switch
                    {
                        0 => new NotFound("constructor"),
                        1 => new NotFound("message constructor"),
                        2 => new NotFound(),
                        3 => new Conflict(new Details(42)),
                        _ => "ok"
                    });
            }
            public static class Probe
            {
                public static async Task<bool> Run()
                {
                    var handler = new Handler();
                    var context = new InvocationContext { Operation = IServiceOperations.Run, HandlerType = typeof(Handler),
                        HandlerMethodInfo = typeof(Handler).GetMethods().First(method => method.Name == "HandleAsync" && method.GetParameters()[0].ParameterType == typeof(Query)) };
                    var constructed = await handler.HandleAsync(new Query(0), context, default);
                    var message = await handler.HandleAsync(new Query(1), context, default);
                    var typed = await handler.HandleAsync(new Query(2), context, default);
                    var conflict = await handler.HandleAsync(new Query(3), context, default);
                    var details = new Details(99);
                    return constructed is IService.RunOperation.NotFound { Message: "constructor" }
                        && message is IService.RunOperation.NotFound { Message: "message constructor" }
                        && typed is IService.RunOperation.NotFound { Message: "" }
                        && conflict is IService.RunOperation.Conflict { Error.Count: 42 }
                        && typeof(Service.Run.NotFound) == typeof(IService.RunOperation.NotFound)
                        && typeof(Service.Run.Result) == typeof(IService.RunOperation.Result)
                        && ReferenceEquals(new Service.Run.Conflict(details).Error, details)
                        && new Service.Run.NotFound().ErrorCode == "404"
                        && typeof(Service.Run.Conflict).GetProperty("ErrorCode") == null
                        && typeof(Service.Run).GetMethods().All(method => !method.Name.StartsWith("Create"))
                        && new Service.Run.Conflict(details).GetType() == typeof(IService.RunOperation.Conflict);
                }
            }
            """;
        var compilation = CSharpCompilation.CreateCompilation("InheritedFailures_" + Guid.NewGuid().ToString("N"), source, true,
            typeof(ApplicationService<>), typeof(ServiceOperation<,>), typeof(FailureCaseAttribute), typeof(Context),
            typeof(ServiceProviderServiceExtensions), typeof(System.Text.Json.Serialization.JsonUnionAttribute));
        ISourceGenerator[] generators = [new NOF.Contract.SourceGenerator.FailureCaseGenerator().AsSourceGenerator(),
            new NOF.Contract.SourceGenerator.OperationDescriptorGenerator().AsSourceGenerator(), new ApplicationServiceGenerator().AsSourceGenerator()];
        if (reverseOrder)
        {
            generators = [.. generators.Reverse()];
        }
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators,
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var result = (Task<bool>)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        Assert.True(await result);
    }
}
