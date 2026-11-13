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
        var types = typeof(ValueObjectLengthConfigurationAnalyzer).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => typeof(IIncrementalGenerator).IsAssignableFrom(type));
        Assert.DoesNotContain(types, type => type.Name == "HttpApplicationServiceAnalyzer");
        Assert.DoesNotContain(new NOF.CodeFixes.AttributeRemovalCodeFixProvider().FixableDiagnosticIds,
            id => id != "NOF020");
        Assert.DoesNotContain(new NOF.CodeFixes.TypeDeclarationCodeFixProvider().FixableDiagnosticIds,
            id => id is "NOF202" or "NOF211");
    }

    [Fact]
    public void ShippedGeneratorAssemblies_DoNotExposeClientGenerators()
    {
        var assemblies = new[]
        {
            typeof(ApplicationServiceGenerator).Assembly,
            typeof(NOF.Contract.SourceGenerator.ApplicationServiceAnalyzer).Assembly,
            typeof(ValueObjectLengthConfigurationAnalyzer).Assembly,
            typeof(ValueObjectLengthConfigurationAnalyzer).Assembly
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
