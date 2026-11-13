using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Abstraction.SourceGenerator;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using NOF.Contract.SourceGenerator;
using NOF.Domain;
using NOF.Domain.SourceGenerator;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.SourceGenerator;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public sealed class AllSourceGeneratorsMonolithTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllGenerators_InOneCompilation_DoNotConsumeOtherGeneratorOutputs(bool reverseOrder)
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using NOF.Abstraction;
            using NOF.Application;
            using NOF.Contract;
            using NOF.Hosting.AspNetCore;
            using NOF.Domain;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Monolith;

            [NewableValueObject]
            public readonly partial struct UserId : IValueObject<long>;

            public sealed record MappingSource(long UserId);

            public sealed class MappingDestination
            {
                public UserId UserId { get; set; }
            }

            [Mappable<MappingSource, MappingDestination>]
            public static partial class AppMappings;

            public interface IClock;

            [AutoInject(ServiceLifetime.Singleton)]
            public sealed class Clock : IClock;

            public sealed record AppEvent(string Value);

            public sealed class AppEventHandler : InMemoryEventHandler<AppEvent>
            {
                public override Task HandleAsync(AppEvent @event, Context context, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }

            public sealed record AppCommand(string Value);

            public sealed record PingRequest(string Value);

            public interface IDemoService : IApplicationService
            {
                [FailureCase("Unavailable", "monolith.unavailable")]
                Empty Ping(PingRequest request);
            }

            public partial class DemoServer : ApplicationService<IDemoService>;

            public sealed class PingHandler : DemoServer.Ping
            {
                public override Task<Result> HandleAsync(PingRequest request, InvocationContext context, CancellationToken cancellationToken)
                    => Task.FromResult<Result>(request.Value switch
                    {
                        "constructor" => new Unavailable("missing"),
                        "factory" => new Unavailable("missing"),
                        _ => Empty.Instance
                    });
            }
            """;

        var compilation = CSharpCompilation.CreateCompilation(
            "Monolith",
            source,
            isDll: true,
            typeof(IServiceCollection),
            typeof(AutoInjectAttribute),
            typeof(AssemblyInitializeAttribute),
            typeof(InMemoryEventHandler<>),
            typeof(MappableAttribute<,>),
            typeof(ApplicationService<>),
            typeof(IApplicationService),
            typeof(NOF.Testing.TestOutcome),
            typeof(IValueObject<>),
            typeof(FailureCaseAttribute),
            typeof(OperationInvoker),
            typeof(System.Text.Json.NOFAbstractionExtensions));
        ISourceGenerator[] generators =
        [
            new AutoInjectGenerator().AsSourceGenerator(),
            new EventHandlerRegistrationGenerator().AsSourceGenerator(),
            new ValueObjectGenerator().AsSourceGenerator(),
            new OperationDescriptorGenerator().AsSourceGenerator(),
            new MappableGenerator().AsSourceGenerator(),
            new ApplicationServiceAutoInjectGenerator().AsSourceGenerator(),
            new ApplicationServiceGenerator().AsSourceGenerator()
        ];
        if (reverseOrder)
        {
            generators = [.. generators.Reverse()];
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var generatedCode = string.Join("\n\n", driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetRoot().ToFullString()));
        Assert.DoesNotContain("ServiceClient", generatedCode);
        Assert.Contains("public static async global::System.Threading.Tasks.Task<global::Monolith.DemoServer.Ping.Result> InvokeAsync", generatedCode);
        Assert.Contains("typeof(global::Monolith.DemoServer.Ping), typeof(global::Monolith.PingHandler)", generatedCode);
    }
}
