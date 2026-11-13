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
        var invalid = await host.CallAsync<object?>(
            static (services, context, ct) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = " " }, context, ct));
        Assert.NotEmpty(Assert.IsType<NOFSampleService.CreateConfigNode.InvalidInput>(InvocationResult.Value(invalid)).Message);

        var parent = await host.CallAsync<object?>(
            static (services, context, ct) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "checked-parent" }, context, ct));
        var id = Assert.IsType<CreatedConfigNode>(InvocationResult.Value(parent)).Id;
        var child = await host.CallAsync<object?>(
            (services, context, ct) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "checked-child", ParentId = id }, context, ct));
        Assert.IsType<CreatedConfigNode>(InvocationResult.Value(child));
        var deleted = await host.CallAsync<object?>(
            (services, context, ct) => NOFSampleService.DeleteConfigNode.InvokeAsync(services,
                new DeleteConfigNodeRequest { Id = id }, context, ct));
        Assert.Equal(id, Assert.IsType<NOFSampleService.DeleteConfigNode.HasChildren>(InvocationResult.Value(deleted)).Error.NodeId);
    }

    [Fact]
    public async Task ServerInvocationHarness_ShouldCreateRootNode_AndQueryItBack()
    {
        await using var host = await CreateHostAsync();

        var createResult = await host.CallAsync<object?>(
            static (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                new CreateConfigNodeRequest { Name = "root-a" },
                context,
                cancellationToken),
            configure: scope => scope
                .SetTenant("tenant-a")
                .SetUser("user-1", "Alice"));

        Assert.NotNull(Assert.IsType<CreatedConfigNode>(InvocationResult.Value(createResult)));

        var roots = await host.CallAsync<object?>(
            static (services, context, cancellationToken) => NOFSampleService.GetRootConfigNodes.InvokeAsync(services,
                new GetRootConfigNodesRequest(),
                context,
                cancellationToken),
            configure: scope => scope
                .SetTenant("tenant-a")
                .SetUser("user-1", "Alice"));

        Assert.NotNull(roots);
        var root = Assert.Single(Assert.IsType<GetRootConfigNodesResponse>(roots).Nodes);
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

            var createParent = await scope.CallAsync<object?>(
                (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                    new CreateConfigNodeRequest { Name = "parent-b" },
                    context,
                    cancellationToken));
            Assert.NotNull(Assert.IsType<CreatedConfigNode>(InvocationResult.Value(createParent)));

            var roots = await scope.CallAsync<object?>(
                (services, context, cancellationToken) => NOFSampleService.GetRootConfigNodes.InvokeAsync(services,
                    new GetRootConfigNodesRequest(),
                    context,
                    cancellationToken));
            var root = Assert.Single(Assert.IsType<GetRootConfigNodesResponse>(roots).Nodes);

            var createChild = await scope.CallAsync<object?>(
                (services, context, cancellationToken) => NOFSampleService.CreateConfigNode.InvokeAsync(services,
                    new CreateConfigNodeRequest { Name = "child-b", ParentId = root.Id },
                    context,
                    cancellationToken));
            Assert.NotNull(Assert.IsType<CreatedConfigNode>(InvocationResult.Value(createChild)));

            return root.Id;
        });

        await host.ExecuteAsync(async scope =>
        {
            scope.SetTenant("tenant-b")
                .SetUser("user-2", "Bob");

            var parent = await scope.CallAsync<object?>(
                (services, context, cancellationToken) => NOFSampleService.GetConfigNodeById.InvokeAsync(services,
                    new GetConfigNodeByIdRequest { Id = parentId },
                    context,
                    cancellationToken));

            Assert.NotNull(Assert.IsType<GetConfigNodeByIdResponse>(InvocationResult.Value(parent)));
            Assert.Equal("parent-b", Assert.IsType<GetConfigNodeByIdResponse>(InvocationResult.Value(parent))!.Node.Name);

            var projection = await scope.GetRequiredService<IPersistenceSession>()
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
        builder.AddApplicationPart(typeof(NOF.Sample.Persistence.ConfigNodePersistence).Assembly);
        builder.AddApplicationService<NOFSampleService>();

        return Task.FromResult(builder.BuildTestHost());
    }
}

[Mappable<ConfigFile, ConfigFileDto>]
[Mappable<ConfigNode, ConfigNodeDto>]
public static partial class SampleHarnessMappings;
