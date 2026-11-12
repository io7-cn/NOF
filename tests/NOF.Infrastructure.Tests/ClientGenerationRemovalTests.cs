using Microsoft.CodeAnalysis;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using NOF.Contract.SourceGenerator;
using NOF.Hosting.AspNetCore;
using NOF.Hosting.SourceGenerator;
using NOF.Infrastructure.SourceGenerator;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class ClientGenerationRemovalTests
{
    [Fact]
    public void ShippedGeneratorAssemblies_DoNotExposeClientGenerators()
    {
        var assemblies = new[]
        {
            typeof(ApplicationServiceGenerator).Assembly,
            typeof(HttpServiceEndpointGenerator).Assembly,
            typeof(OutboundMiddlewareDispatcherInjectionAnalyzer).Assembly
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
