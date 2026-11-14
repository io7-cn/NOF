using Microsoft.CodeAnalysis;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using NOF.Contract.SourceGenerator;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.SourceGenerator;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class ClientGenerationRemovalTests
{
    [Fact]
    public void HostingAnalyzerAssembly_ShouldContainNoHttpRouteGeneratorsOrTransportDiagnostics()
    {
        var types = typeof(FilterFailureCaseGenerator).Assembly.GetTypes();
        Assert.Equal(["FilterFailureCaseGenerator"], types.Where(type => typeof(IIncrementalGenerator).IsAssignableFrom(type)).Select(type => type.Name));
        Assert.DoesNotContain(types, type => type.Name == "HttpApplicationServiceAnalyzer");
        Assert.DoesNotContain(new CodeFixes.AttributeRemovalCodeFixProvider().FixableDiagnosticIds,
            id => id != "NOF020");
        Assert.DoesNotContain(new CodeFixes.TypeDeclarationCodeFixProvider().FixableDiagnosticIds,
            id => id is "NOF202" or "NOF211");
    }

    [Fact]
    public void ShippedGeneratorAssemblies_DoNotExposeClientGenerators()
    {
        var assemblies = new[]
        {
            typeof(ApplicationServiceGenerator).Assembly,
            typeof(Contract.SourceGenerator.ApplicationServiceAnalyzer).Assembly,
            typeof(FilterFailureCaseGenerator).Assembly,
            typeof(FilterFailureCaseGenerator).Assembly
        };
        foreach (var assembly in assemblies.Distinct())
        {
            Assert.DoesNotContain(assembly.GetTypes(), static type =>
                typeof(IIncrementalGenerator).IsAssignableFrom(type) && type.Name.Contains("Client", StringComparison.Ordinal));
        }
        Assert.Null(typeof(IApplicationService).Assembly.GetType("NOF.Contract.IApplicationServiceClient"));
        Assert.Null(typeof(IApplicationService).Assembly.GetType("NOF.Contract.IApplicationServiceClient`1"));
        Assert.Null(typeof(IApplicationService).Assembly.GetType("NOF.Contract.IRequestOutboundPipelineExecutor"));
        Assert.Null(typeof(IApplicationService).Assembly.GetType("NOF.Contract.RequestOutboundHandlerDelegate"));
        Assert.Null(typeof(IApplicationService).Assembly.GetType("NOF.Contract.RequestOutboundContext"));
    }
}
