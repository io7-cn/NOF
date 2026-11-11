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
            using NOF.Domain;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Monolith;

            [Failure("Unavailable", "The operation is unavailable.", "monolith.unavailable")]
            public partial class AppFailures;

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

            public sealed class AppCommandHandler : CommandHandler<AppCommand>
            {
                public override Task HandleAsync(AppCommand command, Context context, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }

            public sealed record PingRequest(string Value);

            [TransportOverHttp(HttpRpcStyle.JsonRpc, "/rpc")]
            public interface IDemoService : IApplicationService
            {
                Result Ping(PingRequest request);
            }

            public partial class DemoServer : ApplicationService<IDemoService>;

            public sealed class PingHandler : DemoServer.Ping
            {
                public override Task<Result> HandleAsync(PingRequest request, Context context, CancellationToken cancellationToken)
                    => Task.FromResult(Result.Success());
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
            typeof(CommandHandler<>),
            typeof(MappableAttribute<,>),
            typeof(ApplicationService<>),
            typeof(IApplicationService),
            typeof(TransportOverHttpAttribute),
            typeof(Result),
            typeof(IValueObject<>),
            typeof(FailureAttribute),
            typeof(ApplicationServiceInvoker),
            typeof(System.Text.Json.NOFAbstractionExtensions));
        ISourceGenerator[] generators =
        [
            new AutoInjectGenerator().AsSourceGenerator(),
            new EventHandlerRegistrationGenerator().AsSourceGenerator(),
            new FailureGenerator().AsSourceGenerator(),
            new ValueObjectGenerator().AsSourceGenerator(),
            new HandlerRegistrationGenerator().AsSourceGenerator(),
            new MappableGenerator().AsSourceGenerator(),
            new ApplicationServiceAutoInjectGenerator().AsSourceGenerator(),
            new ApplicationServiceGenerator().AsSourceGenerator(),
            new ApplicationServiceClientGenerator().AsSourceGenerator(),
            new HttpRpcClientGenerator().AsSourceGenerator(),
            new LocalApplicationServiceClientGenerator().AsSourceGenerator()
        ];
        if (reverseOrder)
        {
            generators = [.. generators.Reverse()];
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var generatedCode = string.Join("\n\n", driver.GetRunResult().GeneratedTrees.Select(static tree => tree.GetRoot().ToFullString()));
        Assert.Contains("interface IDemoServiceClient : global::NOF.Contract.IApplicationServiceClient<global::Monolith.IDemoService>", generatedCode);
        Assert.Contains("partial class HttpDemoServiceClient : IDemoServiceClient", generatedCode);
        Assert.Contains("class LocalDemoServerClient : global::Monolith.IDemoServiceClient", generatedCode);
        Assert.Contains("typeof(global::Monolith.DemoServer.Ping), typeof(global::Monolith.PingHandler)", generatedCode);
    }
}
