using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Application;
using NOF.Sample.Endpoints;
using NOF.Sample.UI.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class ConfigNodeRestTests
{
    [Fact]
    public async Task NativeModule_ShouldCreateReadMoveAndDeleteRealNodes()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var parent = await CreateAsync(http, "parent");
        var child = await CreateAsync(http, "child", parent);
        var resource = await http.GetFromJsonAsync<ConfigNodeDto>($"/api/config-nodes/{child}");
        Assert.Equal("child", resource!.Name);
        Assert.Equal(parent, resource.ParentId);
        var children = await http.GetFromJsonAsync<GetConfigNodeChildrenResponse>($"/api/config-nodes/{parent}/children");
        Assert.Equal([child], children!.ChildrenIds);

        using var blockedDelete = await http.DeleteAsync($"/api/config-nodes/{parent}");
        Assert.Equal(HttpStatusCode.Conflict, blockedDelete.StatusCode);
        using var move = await http.PatchAsJsonAsync($"/api/config-nodes/{child}/parent", new { parentId = (long?)null, nodeId = parent });
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);
        var roots = await http.GetFromJsonAsync<List<ConfigNodeDto>>("/api/config-nodes");
        Assert.Equal(2, roots!.Count);
        Assert.Null(roots.Single(node => node.Id == child).ParentId);

        using var deleted = await http.DeleteAsync($"/api/config-nodes/{parent}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await http.GetAsync($"/api/config-nodes/{parent}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "config_node.not_found");
        using var deleteChild = await http.DeleteAsync($"/api/config-nodes/{child}");
        Assert.Equal(HttpStatusCode.NoContent, deleteChild.StatusCode);
    }

    [Fact]
    public async Task NativeModule_ShouldMapBusinessFailuresAndRejectMalformedInput()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var id = await CreateAsync(http, "existing");
        using var duplicate = await http.PostAsJsonAsync("/api/config-nodes", new { name = "existing" });
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "config_node.name_conflict");
        using var unknownParent = await http.PostAsJsonAsync("/api/config-nodes", new { name = "orphan", parentId = long.MaxValue });
        await AssertProblemAsync(unknownParent, HttpStatusCode.UnprocessableEntity, "config_node.parent_not_found");
        using var self = await http.PatchAsJsonAsync($"/api/config-nodes/{id}/parent", new { parentId = id });
        await AssertProblemAsync(self, HttpStatusCode.Conflict, "config_node.parent_cycle");
        var child = await CreateAsync(http, "child", id);
        using var cycle = await http.PatchAsJsonAsync($"/api/config-nodes/{id}/parent", new { parentId = child });
        await AssertProblemAsync(cycle, HttpStatusCode.Conflict, "config_node.parent_cycle");
        using var missingParentField = await http.PatchAsJsonAsync($"/api/config-nodes/{child}/parent", new { nodeId = id });
        Assert.Equal(HttpStatusCode.BadRequest, missingParentField.StatusCode);
        using var blank = await http.PostAsJsonAsync("/api/config-nodes", new { name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using var blankProblem = JsonDocument.Parse(await blank.Content.ReadAsStringAsync());
        Assert.Equal("Node name cannot be blank.", blankProblem.RootElement.GetProperty("detail").GetString());
        using var invalidJson = await http.PutAsJsonAsync($"/api/config-nodes/{id}/files/settings.json", new { content = "{" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidJson.StatusCode);
        using var contentProblem = JsonDocument.Parse(await invalidJson.Content.ReadAsStringAsync());
        Assert.Equal("Invalid JSON format.", contentProblem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UiHttpAdapter_ShouldUseTheRealRestModule_ForFilesAndQueries()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var api = new HttpSampleApi(http);
        Assert.IsType<Empty>((await api.CreateConfigNodeAsync(new() { Name = "ui" }, Context.Empty)).Value);
        var root = Assert.Single((await api.GetRootConfigNodesAsync(new(), Context.Empty)).ExpectSuccess().Nodes);
        Assert.IsType<Empty>((await api.AddOrUpdateConfigFileAsync(new()
        { NodeId = root.Id, FileName = "settings.json", Content = "{\"port\":8080}" }, Context.Empty)).Value);
        Assert.IsType<Empty>((await api.SetActiveFileAsync(new()
        { NodeId = root.Id, FileName = "settings.json" }, Context.Empty)).Value);
        var node = (await api.GetConfigNodeByIdAsync(new() { Id = root.Id }, Context.Empty)).ExpectSuccess().Node;
        Assert.Equal("settings.json", node.ActiveFileName);
        Assert.Single(node.ConfigFiles);
        Assert.IsType<Empty>((await api.RemoveConfigFileAsync(new()
        { NodeId = root.Id, FileName = "settings.json" }, Context.Empty)).Value);
        Assert.IsType<Empty>((await api.DeleteConfigNodeAsync(new() { Id = root.Id }, Context.Empty)).Value);
        Assert.Equal("config_node.not_found",
            (await api.GetConfigNodeByIdAsync(new() { Id = root.Id }, Context.Empty)).ExpectFailure().Title);
    }

    [Fact]
    public async Task NativeModule_ShouldKeepTenantIsolationAndNotExposeLegacyRoutes()
    {
        await using var app = await CreateAppAsync(relational: true);
        using var first = app.GetTestClient();
        first.DefaultRequestHeaders.Add(NOFAbstractionConstants.Transport.Headers.TenantId, "tenant-a");
        var id = await CreateAsync(first, "private");
        using var second = app.GetTestClient();
        second.DefaultRequestHeaders.Add(NOFAbstractionConstants.Transport.Headers.TenantId, "tenant-b");
        using var invisible = await second.GetAsync($"/api/config-nodes/{id}");
        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        using var oldRoute = await first.PostAsJsonAsync("/rpc/NOFSample/CreateConfigNode", new { name = "legacy" });
        Assert.Equal(HttpStatusCode.NotFound, oldRoute.StatusCode);
    }

    [Fact]
    public async Task NativeBridge_ShouldPreservePolicyRejectionAndOpenApiMetadata()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        using var protectedResponse = await http.GetAsync("/policy-probe");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        Assert.NotEmpty(protectedResponse.Headers.WwwAuthenticate);
        Assert.Equal(0, app.Services.GetRequiredService<PolicyProbe>().Constructed);
        using var document = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        var create = paths.GetProperty("/api/config-nodes").GetProperty("post");
        Assert.True(create.GetProperty("responses").TryGetProperty("201", out _));
        var move = paths.GetProperty("/api/config-nodes/{id}/parent").GetProperty("patch");
        Assert.True(move.GetProperty("responses").TryGetProperty("204", out _));
        Assert.DoesNotContain("nodeId", move.GetProperty("requestBody").GetRawText());
    }

    [Fact]
    public void ContractAssembly_ShouldContainNoHttpAnnotationsOrRpcOutcomeInterface()
    {
        var contract = typeof(IApplicationService).Assembly;
        Assert.Null(contract.GetType("NOF.Contract.HttpEndpointAttribute"));
        Assert.Null(contract.GetType("NOF.Contract.TransportOverHttpAttribute"));
        Assert.Null(contract.GetType("NOF.Contract.IRpcResponse`1"));
        Assert.DoesNotContain(typeof(INOFSampleService).GetCustomAttributes(false),
            static attribute => attribute.GetType().Name.StartsWith("TransportOver", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(INOFSampleService).Assembly.GetReferencedAssemblies(),
            static assembly => assembly.Name!.StartsWith("NOF.Hosting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UiHttpAdapter_ShouldReadNativeSseAndProblemDetails()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var api = new HttpSampleApi(http);
        var result = await api.WatchSampleStreamAsync(new() { Topic = "閰嶇疆鏇存柊", Count = 2, IntervalMilliseconds = 1 }, Context.Empty);
        var items = new List<SampleStreamEvent>();
        await foreach (var item in result.ExpectSuccess())
        {
            items.Add(item);
        }
        Assert.Equal(4, items.Count);
        Assert.Equal([0, 1, 2, 3], items.Select(item => item.Index));
        Assert.All(items, item => Assert.Equal("閰嶇疆鏇存柊", item.Topic));
        var failure = await api.WatchSampleStreamAsync(new() { ForceFailure = true }, Context.Empty);
        Assert.NotEmpty(failure.ExpectFailure().Detail);
    }

    private static async Task<long> CreateAsync(HttpClient http, string name, long? parentId = null)
    {
        using var response = await http.PostAsJsonAsync("/api/config-nodes", new { name, parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedConfigNode>())!;
        Assert.EndsWith($"/api/config-nodes/{created.Id}", response.Headers.Location!.ToString());
        using var follow = await http.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
        return created.Id;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("title").GetString());
    }

    private static async Task<WebApplication> CreateAppAsync(bool relational = false)
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
        builder.AddApplicationPart(typeof(ConfigNodeRestTests).Assembly);
        builder.AddApplicationPart(typeof(NOF.Sample.Persistence.ConfigNodePersistence).Assembly);
        builder.AddApplicationService<NOFSampleService>();
        builder.AddApplicationService<PolicyProbeService>();
        builder.Services.AddSingleton<PolicyProbe>();
        builder.Services.Configure<CacheServiceOptions>(options => options.KeyPrefix = "rest-test:{tenantId}:");
        builder.Services.AddProblemDetails();
        if (relational)
        {
            builder.UseDbContext<RestConfigDbContext>()
                .WithTenantMode(TenantMode.DatabasePerTenant)
                .WithConnectionString($"Data Source=rest-{Guid.NewGuid():N}-{{tenantId}};Mode=Memory;Cache=Shared")
                .WithOptions(static (options, connection) => options.UseSqlite(connection));
        }
        var app = builder.Build();
        app.UseNOF();
        app.MapConfigNodeEndpoints();
        app.MapGet("/policy-probe", PolicyProbeService.Read.Descriptor);
        app.UseStatusCodePages();
        app.MapOpenApi();
        await app.StartAsync();
        return app;
    }
}

public sealed class RestConfigDbContext(DbContextOptions<RestConfigDbContext> options) : NOFDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ConfigNode>(node =>
        {
            node.HasKey(value => value.Id);
            node.OwnsMany(value => value.ConfigFiles, file =>
            {
                file.WithOwner().HasForeignKey("NodeId");
                file.Property<long>("Id").ValueGeneratedOnAdd();
                file.HasKey("Id");
            });
        });
        model.Entity<NOF.Sample.Application.Entities.ConfigNodeChildren>().HasKey(value => value.NodeId);
    }
}

public interface IPolicyProbeService : IApplicationService
{
    [RequirePermission("config.read")]
    string Read(Empty request);
}

public partial class PolicyProbeService : ApplicationService<IPolicyProbeService>;
public sealed class PolicyProbe { public int Constructed { get; set; } }

public sealed class PolicyProbeHandler : PolicyProbeService.Read
{
    public PolicyProbeHandler(PolicyProbe probe) => probe.Constructed++;
    public override Task<string> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult("secret");
}
