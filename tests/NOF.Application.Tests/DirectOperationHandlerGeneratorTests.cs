using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Application.SourceGenerator;
using System.Reflection;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public sealed class DirectOperationHandlerGeneratorTests
{
    [Fact]
    public void ApplicationPart_RegistersIndependentHandlersWithoutContractsPartialOrConstruction()
    {
        var source = """
            #nullable enable
            using NOF.Application;
            using NOF.Abstraction;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Direct;
            public record Created(int Id);
            public abstract class Reaction<T> : OperationHandler<T>;
            public sealed class AwardPoints : Reaction<Created>
            {
                public AwardPoints() => throw new System.Exception("Description constructed the handler");
                public override Task HandleAsync(Created request, InvocationContext context, CancellationToken ct) => Task.CompletedTask;
            }
            public sealed class Audit : OperationHandler<Created>
            {
                public override Task HandleAsync(Created request, InvocationContext context, CancellationToken ct) => Task.CompletedTask;
            }
            public sealed class Find : OperationHandler<Created, string?>
            {
                public override Task<string?> HandleAsync(Created request, InvocationContext context, CancellationToken ct) => Task.FromResult<string?>(null);
            }
            public union Decision(string, Failure);
            public sealed class Decide : OperationHandler<Created, Decision>
            {
                public override Task<Decision> HandleAsync(Created request, InvocationContext context, CancellationToken ct)
                    => Task.FromResult<Decision>(new Failure("missing", "No value"));
            }
            """;
        var output = Generate(source, out var diagnostics);
        Assert.Empty(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var services = new ServiceCollection();
        var initializer = assembly.GetCustomAttributes<AssemblyInitializeAttribute>().Single();
        initializer.Initialize(services);
        initializer.Initialize(services);
        var registry = services.GetOrAddSingleton<ApplicationServiceRegistry>();
        var points = registry.GetOperation(assembly.GetType("Direct.AwardPoints")!);
        var audit = registry.GetOperation(assembly.GetType("Direct.Audit")!);
        var find = registry.GetOperation(assembly.GetType("Direct.Find")!);
        Assert.Equal("Direct.AwardPoints", points.Id);
        Assert.NotEqual(points.Id, audit.Id);
        Assert.Equal(points.RequestType, audit.RequestType);
        Assert.Null(points.ContractType);
        Assert.True(points.IsOneWay);
        Assert.Empty(points.ResultCases);
        Assert.True(find.AllowsNullResponse);
        Assert.False(find.IsOneWay);
        var decision = registry.GetOperation(assembly.GetType("Direct.Decide")!);
        Assert.Contains(decision.ResultCases, item => item.CaseType == typeof(Failure) && item.IsFailure);
        Assert.Contains(decision.ResultCases, item => item.CaseType == typeof(string) && !item.IsFailure);
        // Configuration lookups do not freeze later feature-module declarations.
        registry.Add(new(typeof(IDisposable), typeof(object)) { HandlerMappings = new Dictionary<string, OperationMapping>() });
        Assert.Equal(4, registry.GetOperations().Count());
        Assert.Throws<InvalidOperationException>(() => registry.AddOperation(registry.GetMapping(points)));
        Assert.Single(services, item => item.ServiceType == points.DeclaringType);
    }

    [Fact]
    public void InaccessibleHandler_ReportsAnActionableDiagnostic()
    {
        _ = Generate("""
            using NOF.Application;
            using System.Threading;
            using System.Threading.Tasks;
            public class Container
            {
                private sealed class Hidden : OperationHandler<string>
                {
                    public override Task HandleAsync(string request, InvocationContext context, CancellationToken ct) => Task.CompletedTask;
                }
            }
            """, out var diagnostics);
        Assert.Contains(diagnostics, item => item.Id == "NOF301");
    }

    private static Compilation Generate(string source, out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics)
    {
        var input = CSharpCompilation.CreateCompilation("Direct" + Guid.NewGuid().ToString("N"), source, isDll: true,
            typeof(OperationHandler<>), typeof(ServiceDescriptor), typeof(AssemblyInitializeAttribute), typeof(InitializedTypes),
            typeof(System.Text.Json.Serialization.JsonUnionAttribute));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ApplicationServiceAutoInjectGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)input.SyntaxTrees.First().Options);
        _ = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out diagnostics);
        return output;
    }
}
