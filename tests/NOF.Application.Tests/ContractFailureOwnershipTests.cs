using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using Xunit;

namespace NOF.Application.Tests;

public sealed class ContractFailureOwnershipTests
{
    [Fact]
    public void RemoteProducer_ReferencesOnlyContractMetadataAndFrameworkApplication()
    {
        var contract = CSharpCompilation.CreateCompilation("ProducerContract_" + Guid.NewGuid().ToString("N"), """
            using NOF.Contract;
            namespace Business;
            public sealed record Query;
            public interface IService : IApplicationService { Empty Run(Query request); }
            """, true, typeof(IApplicationService));
        contract = contract.RemoveReferences(contract.References.Where(reference =>
            Path.GetFileNameWithoutExtension(reference.Display) is { } name
            && name.StartsWith("NOF.", StringComparison.Ordinal) && name != "NOF.Contract"));
        using var image = new MemoryStream();
        var emitted = contract.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var producer = CSharpCompilation.CreateCompilation("RemoteProducer", """
            using NOF.Application;
            public static class Producer
            {
                public static ServiceOperationReference<Business.Query> Target { get; }
                    = new(typeof(Business.IService), nameof(Business.IService.Run));
            }
            """, true, typeof(ServiceOperationReference<>), typeof(IApplicationService))
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        Assert.DoesNotContain(producer.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Null(producer.GetTypeByMetadataName("Business.IServiceOperations"));
        var target = producer.GetTypeByMetadataName("Producer")!.GetMembers("Target").OfType<IPropertySymbol>().Single();
        Assert.Equal("NOF.Application", target.Type.ContainingAssembly.Name);
    }

    [Fact]
    public void MetadataContract_GeneratesOutcomesOnlyInApplication()
    {
        var contract = CSharpCompilation.CreateCompilation("FailureContract_" + Guid.NewGuid().ToString("N"), """
            using NOF.Contract;
            namespace Business;
            public sealed record Query;
            public sealed record Details(int Value);
            public interface IService : IApplicationService
            {
                [FailureCase("NotFound", "404")]
                [FailureCase("Conflict", typeof(Details))]
                Empty Run(Query request);
            }
            """, true, typeof(IApplicationService));
        // Compile Contract independently, even if other tests loaded runtime assemblies first.
        contract = contract.RemoveReferences(contract.References.Where(reference =>
            Path.GetFileNameWithoutExtension(reference.Display) is { } name
            && name.StartsWith("NOF.", StringComparison.Ordinal) && name != "NOF.Contract"));
        using var image = new MemoryStream();
        var contractEmit = contract.Emit(image);
        Assert.True(contractEmit.Success, string.Join("\n", contractEmit.Diagnostics));
        Assert.Empty(contract.GetTypeByMetadataName("Business.IService")!.GetTypeMembers());
        var application = CSharpCompilation.CreateCompilation("FailureApplication", """
            using NOF.Application;
            public partial class Service : ApplicationService<Business.IService>;
            public partial class OtherService : ApplicationService<Business.IService>;
            """, true, typeof(ApplicationService<>), typeof(Context), typeof(ServiceProviderServiceExtensions), typeof(System.Text.Json.Serialization.JsonUnionAttribute))
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ApplicationServiceGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)application.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(application, out var generated, out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(generated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var operation = generated.GetTypeByMetadataName("Service+Run")!;
        Assert.Equal("FailureApplication", operation.GetTypeMembers("Result").Single().ContainingAssembly.Name);
        Assert.Equal("FailureApplication", operation.GetTypeMembers("NotFound").Single().ContainingAssembly.Name);
        Assert.Equal("FailureApplication", operation.GetTypeMembers("Conflict").Single().ContainingAssembly.Name);
        Assert.NotEqual(operation.GetTypeMembers("Result").Single(), generated.GetTypeByMetadataName("OtherService+Run")!.GetTypeMembers("Result").Single());
        Assert.Empty(generated.GetTypeByMetadataName("Business.IService")!.GetTypeMembers());
    }
}
