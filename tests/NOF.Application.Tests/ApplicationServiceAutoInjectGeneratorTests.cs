using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class ApplicationServiceAutoInjectGeneratorTests
{
    [Fact]
    public void GeneratedCode_RegistersServiceOperationsOnly()
    {
        const string source = """
            using NOF.Application;
            using NOF.Abstraction;
            using NOF.Contract;
            using System.Threading;
            using System.Threading.Tasks;

            namespace App
            {
                public record PingRequest(string Value);

                public interface IMyService : IApplicationService
                {
                    NOF.Testing.TestOutcome Ping(PingRequest request);
                }

                public partial class MyService : ApplicationService<IMyService>;

                public sealed class PingHandler : MyService.Ping
                {
                    public override Task<NOF.Testing.TestOutcome> HandleAsync(PingRequest request, InvocationContext context, CancellationToken cancellationToken)
                        => Task.FromResult(NOF.Testing.TestOutcome.Success());
                }
            }
            """;

        var compilation = CSharpCompilation.CreateCompilation("App", source, isDll: true,
            typeof(ApplicationService<>),
            typeof(ServiceOperation<,>),
            typeof(IApplicationService),
            typeof(Context),
            typeof(NOF.Testing.TestOutcome),
            typeof(NOF.Testing.TestOutcome<>),
            typeof(ServiceDescriptor),
            typeof(AssemblyInitializeAttribute),
            typeof(ApplicationServiceRegistration),
            typeof(InitializedTypes));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([
            new NOF.Contract.SourceGenerator.OperationDescriptorGenerator().AsSourceGenerator(), new ApplicationServiceGenerator().AsSourceGenerator(),
            new ApplicationServiceAutoInjectGenerator().AsSourceGenerator()
        ], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = string.Join("\n\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetRoot().ToFullString()));

        Assert.Contains("AssemblyInitializeAttribute<global::App.__AppApplicationServiceAutoInjectAssemblyInitializer>", generatedCode);
        Assert.Contains("services.InitializedTypes.Add(typeof(__AppApplicationServiceAutoInjectAssemblyInitializer))", generatedCode);
        Assert.Contains("services.Add(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(global::App.MyService.Ping), typeof(global::App.PingHandler)));", generatedCode);
        Assert.DoesNotContain("ApplicationServiceRegistration", generatedCode);
        Assert.DoesNotContain("ServiceDescriptor.Scoped(typeof(global::App.MyService)", generatedCode);
    }

    [Fact]
    public void GeneratedCode_DoesNotRegister_UnrelatedInterfaces()
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            using NOF.Abstraction;

            namespace App
            {
                public interface IMyService : IApplicationService
                {
                    NOF.Testing.TestOutcome Run(RunRequest request);
                }

                public record RunRequest(string Value);

                public partial class MyService : ApplicationService<IMyService>;

                public interface IUnrelated
                {
                    NOF.Testing.TestOutcome Run(RunRequest request);
                }

                public sealed class UnrelatedHandler : IUnrelated
                {
                    public NOF.Testing.TestOutcome Run(RunRequest request) => NOF.Testing.TestOutcome.Success();
                }
            }
            """;

        var compilation = CSharpCompilation.CreateCompilation("App", source, isDll: true,
            typeof(ApplicationService<>),
            typeof(ServiceOperation<,>),
            typeof(IApplicationService),
            typeof(NOF.Testing.TestOutcome),
            typeof(NOF.Testing.TestOutcome<>),
            typeof(ServiceDescriptor),
            typeof(AssemblyInitializeAttribute),
            typeof(ApplicationServiceRegistration),
            typeof(InitializedTypes));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([
            new NOF.Contract.SourceGenerator.OperationDescriptorGenerator().AsSourceGenerator(), new ApplicationServiceGenerator().AsSourceGenerator(),
            new ApplicationServiceAutoInjectGenerator().AsSourceGenerator()
        ], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = string.Join("\n\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetRoot().ToFullString()));
        Assert.DoesNotContain("UnrelatedHandler", generatedCode);
    }

    [Fact]
    public void GeneratedCode_DoesNotRequireApplicationServiceGeneratorOutputAsInput()
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            using NOF.Abstraction;

            namespace App;

            public record PingRequest(string Value);

            public interface IMyService : IApplicationService
            {
                NOF.Testing.TestOutcome Ping(PingRequest request);
            }

            public partial class MyService : ApplicationService<IMyService>;

            public sealed class PingHandler : MyService.Ping;
            """;

        var compilation = CSharpCompilation.CreateCompilation("App", source, isDll: true,
            typeof(ApplicationService<>),
            typeof(ServiceOperation<,>),
            typeof(IApplicationService),
            typeof(NOF.Testing.TestOutcome),
            typeof(ServiceDescriptor),
            typeof(AssemblyInitializeAttribute),
            typeof(InitializedTypes));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ApplicationServiceAutoInjectGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);

        driver = driver.RunGenerators(compilation);

        var generatedCode = string.Join("\n\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetRoot().ToFullString()));
        Assert.Contains("typeof(global::App.MyService.Ping), typeof(global::App.PingHandler)", generatedCode);
    }
}
