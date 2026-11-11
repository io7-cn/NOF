using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using NOF.Sample.Application;
using NOF.Sample.Application.Entities;
using NOF.Test;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class NOFSampleHarnessTests
{
    [Fact]
    public async Task ServerInvocationHarness_ShouldCreateRootNode_AndQueryItBack()
    {
        await using var host = await CreateHostAsync();

        var createResult = await host.CallAsync<Result>(
            static (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "root-a" },
                context,
                cancellationToken),
            configure: scope => scope
                .SetTenant("tenant-a")
                .SetUser("user-1", "Alice"));

        Assert.NotNull(createResult.ExpectSuccess());

        var roots = await host.CallAsync<GetRootConfigNodesResponse>(
            static (services, context, cancellationToken) => NOFSampleService.GetRootConfigNodes.InvokeAsync(services,
                new GetRootConfigNodesRequest(),
                context,
                cancellationToken),
            configure: scope => scope
                .SetTenant("tenant-a")
                .SetUser("user-1", "Alice"));

        Assert.NotNull(roots);
        var root = Assert.Single(roots.Nodes);
        Assert.Equal("root-a", root.Name);
        Assert.Null(root.ParentId);
    }

    [Fact]
    public async Task ServerInvocationHarness_ShouldPersistTenantData_AndTriggerInMemoryProjection()
    {
        await using var host = await CreateHostAsync();

        var parentId = await host.ExecuteAsync(async scope =>
        {
            scope.SetTenant("tenant-b")
                .SetUser("user-2", "Bob");

            var createParent = await scope.CallAsync<Result>(
                (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                    new CreateConfigNodeRequest { Name = "parent-b" },
                    context,
                    cancellationToken));
            Assert.NotNull(createParent.ExpectSuccess());

            var roots = await scope.CallAsync<GetRootConfigNodesResponse>(
                (services, context, cancellationToken) => NOFSampleService.GetRootConfigNodes.InvokeAsync(services,
                    new GetRootConfigNodesRequest(),
                    context,
                    cancellationToken));
            var root = Assert.Single(roots.Nodes);

            var createChild = await scope.CallAsync<Result>(
                (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                    new CreateConfigNodeRequest { Name = "child-b", ParentId = root.Id },
                    context,
                    cancellationToken));
            Assert.NotNull(createChild.ExpectSuccess());

            return root.Id;
        });

        await host.ExecuteAsync(async scope =>
        {
            scope.SetTenant("tenant-b")
                .SetUser("user-2", "Bob");

            var parent = await scope.CallAsync<Result<GetConfigNodeByIdResponse>>(
                (services, context, cancellationToken) => NOFSampleService.GetConfigNodeById.InvokeAsync(services,
                    new GetConfigNodeByIdRequest { Id = parentId },
                    context,
                    cancellationToken));

            Assert.NotNull(parent.ExpectSuccess());
            Assert.Equal("parent-b", parent.ExpectSuccess()!.Node.Name);

            var projection = await scope.GetRequiredService<IDbContext>()
                .Set<ConfigNodeChildren>()
                .FirstOrDefaultAsync(entity => entity.NodeId == ConfigNodeId.Of(parentId));

            Assert.NotNull(projection);
            Assert.Single(projection.ChildrenIds);
        });
    }

    private static async Task<NOFTestHost> CreateHostAsync()
    {
        var builder = NOFTestAppBuilder.Create()
            .AddApplicationPartOf<NOFSampleService>()
            .AddApplicationPart(typeof(SampleHarnessMappings).Assembly)
            .AddApplicationService<NOFSampleService>()
            .AddInMemoryPersistence();

        return await builder.BuildTestHostAsync();
    }
}

[Mappable<ConfigFile, ConfigFileDto>]
[Mappable<ConfigNode, ConfigNodeDto>]
public static partial class SampleHarnessMappings;
