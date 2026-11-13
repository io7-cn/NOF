using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Sample.Application;
using NOF.Sample.Application.Entities;
using NOF.Test;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class NOFSampleHarnessTests
{
    [Fact]
    public async Task ExpectedFailures_ShouldReturnDeclaredCasesAndTypedData_WithoutDomainExceptions()
    {
        await using var host = await CreateHostAsync();
        var invalid = await host.CallAsync<INOFSampleService.CreateConfigNodeOperation.Result>(
            static (services, context, ct) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = " " }, context, ct));
        Assert.NotEmpty(Assert.IsType<INOFSampleService.CreateConfigNodeOperation.InvalidInput>(invalid.Value).Message);

        var parent = await host.CallAsync<INOFSampleService.CreateConfigNodeOperation.Result>(
            static (services, context, ct) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "checked-parent" }, context, ct));
        var id = Assert.IsType<CreatedConfigNode>(parent.Value).Id;
        var child = await host.CallAsync<INOFSampleService.CreateConfigNodeOperation.Result>(
            (services, context, ct) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "checked-child", ParentId = id }, context, ct));
        Assert.IsType<CreatedConfigNode>(child.Value);
        var deleted = await host.CallAsync<INOFSampleService.DeleteConfigNodeOperation.Result>(
            (services, context, ct) => NOFSampleService.DeleteConfigNode.InvokeAsync(services,
                new DeleteConfigNodeRequest { Id = id }, context, ct));
        Assert.Equal(id, Assert.IsType<INOFSampleService.DeleteConfigNodeOperation.HasChildren>(deleted.Value).Error.NodeId);
    }

    [Fact]
    public async Task ServerInvocationHarness_ShouldCreateRootNode_AndQueryItBack()
    {
        await using var host = await CreateHostAsync();

        var createResult = await host.CallAsync<INOFSampleService.CreateConfigNodeOperation.Result>(
            static (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "root-a" },
                context,
                cancellationToken),
            configure: scope => scope
                .SetTenant("tenant-a")
                .SetUser("user-1", "Alice"));

        Assert.NotNull(Assert.IsType<CreatedConfigNode>(createResult.Value));

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

            var createParent = await scope.CallAsync<INOFSampleService.CreateConfigNodeOperation.Result>(
                (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                    new CreateConfigNodeRequest { Name = "parent-b" },
                    context,
                    cancellationToken));
            Assert.NotNull(Assert.IsType<CreatedConfigNode>(createParent.Value));

            var roots = await scope.CallAsync<GetRootConfigNodesResponse>(
                (services, context, cancellationToken) => NOFSampleService.GetRootConfigNodes.InvokeAsync(services,
                    new GetRootConfigNodesRequest(),
                    context,
                    cancellationToken));
            var root = Assert.Single(roots.Nodes);

            var createChild = await scope.CallAsync<INOFSampleService.CreateConfigNodeOperation.Result>(
                (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                    new CreateConfigNodeRequest { Name = "child-b", ParentId = root.Id },
                    context,
                    cancellationToken));
            Assert.NotNull(Assert.IsType<CreatedConfigNode>(createChild.Value));

            return root.Id;
        });

        await host.ExecuteAsync(async scope =>
        {
            scope.SetTenant("tenant-b")
                .SetUser("user-2", "Bob");

            var parent = await scope.CallAsync<INOFSampleService.GetConfigNodeByIdOperation.Result>(
                (services, context, cancellationToken) => NOFSampleService.GetConfigNodeById.InvokeAsync(services,
                    new GetConfigNodeByIdRequest { Id = parentId },
                    context,
                    cancellationToken));

            Assert.NotNull(Assert.IsType<GetConfigNodeByIdResponse>(parent.Value));
            Assert.Equal("parent-b", Assert.IsType<GetConfigNodeByIdResponse>(parent.Value)!.Node.Name);

            var projection = await scope.GetRequiredService<IDbContext>()
                .Set<ConfigNodeChildren>()
                .FirstOrDefaultAsync(entity => entity.NodeId == ConfigNodeId.From(parentId));

            Assert.NotNull(projection);
            Assert.Single(projection.ChildrenIds);
        });
    }

    private static Task<NOFTestHost> CreateHostAsync()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder().AddNOFTest();
        builder.AddApplicationPartOf<NOFSampleService>();
        builder.AddApplicationPart(typeof(SampleHarnessMappings).Assembly);
        builder.AddApplicationService<NOFSampleService>();

        return Task.FromResult(builder.BuildTestHost());
    }
}

[Mappable<ConfigFile, ConfigFileDto>]
[Mappable<ConfigNode, ConfigNodeDto>]
public static partial class SampleHarnessMappings;
