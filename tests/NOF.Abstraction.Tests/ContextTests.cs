using NOF.Abstraction;
using System.Text.Json.Nodes;
using Xunit;

namespace NOF.Abstraction.Tests;

public class ContextTests
{
    [Fact]
    public void StringItemKeys_ShouldBeCaseInsensitive()
    {
        var context = Context.Empty.WithItem("x-tenant-id", "tenant-a");

        Assert.True(context.TryGetItem("X-Tenant-Id", out var tenantId));
        Assert.Equal("tenant-a", tenantId?.GetValue<string>());
        Assert.True(context.Items.ContainsKey("X-TENANT-ID"));
    }

    [Fact]
    public void WithItem_WithEquivalentStringKey_ShouldReplaceExistingItem()
    {
        var context = Context.Empty
            .WithItem("X-Tenant-Id", "tenant-a")
            .WithItem("x-tenant-id", "tenant-b");

        var item = Assert.Single(context.Items);
        Assert.Equal("tenant-b", item.Value?.GetValue<string>());
    }

    [Fact]
    public void WithoutItem_WithEquivalentStringKey_ShouldRemoveExistingItem()
    {
        var context = Context.Empty
            .WithItem("x-tenant-id", "tenant-a")
            .WithoutItem("X-Tenant-Id");

        Assert.Empty(context.Items);
    }

    [Fact]
    public void JsonSnapshots_PreserveScalarsAndRejectNestedValues_WithoutLeakingMutation()
    {
        var source = new JsonObject { ["text"] = "value", ["number"] = 123, ["boolean"] = true, ["null"] = null };
        var context = Context.Empty.WithItems(source);
        source["text"] = "changed";
        var snapshot = context.Items;
        snapshot["text"] = "changed again";
        Assert.Null(context["text"]!.Parent);
        Assert.Equal("value", context["text"]!.GetValue<string>());
        var restored = Context.Empty.WithItems(JsonNode.Parse(context.Items.ToJsonString())!.AsObject());
        Assert.Equal(123, restored["number"]!.GetValue<int>());
        Assert.True(restored["boolean"]!.GetValue<bool>());
        Assert.True(restored.TryGetItem("NULL", out var empty));
        Assert.Null(empty);
        Assert.Throws<ArgumentException>(() => context.WithItem("array", new JsonArray(1, 2)));
        Assert.Throws<ArgumentException>(() => context.WithItem("object", new JsonObject { ["key"] = "value" }));
        Assert.Throws<ArgumentException>(() => context.WithItem("invalid-number", double.NaN));
    }

    [Fact]
    public void TenantId_ShouldBeNormalizedAndDefaultToHost()
    {
        Assert.Equal("host", Context.Empty.TenantId);
        Assert.Equal("tenanta", Context.Empty.WithTenantId(" tenanta ").TenantId);
    }

    [Fact]
    public async Task PushCurrent_ShouldFlowAcrossAwaitAndRestorePreviousContext()
    {
        var previous = Context.Empty.WithTenantId("previous");
        var current = Context.Empty.WithTenantId("current");

        using (Context.PushCurrent(previous))
        {
            using (Context.PushCurrent(current))
            {
                await Task.Yield();
                Assert.Same(current, Context.Current);
            }

            Assert.Same(previous, Context.Current);
        }

        Assert.Same(Context.Empty, Context.Current);
    }

    [Fact]
    public async Task PushCurrent_ShouldIsolateParallelAsyncFlows()
    {
        var tenantIds = await Task.WhenAll(
            observeTenantAsync("tenanta"),
            observeTenantAsync("tenantb"));

        Assert.Equal(["tenanta", "tenantb"], tenantIds);
        Assert.Same(Context.Empty, Context.Current);

        static async Task<string> observeTenantAsync(string tenantId)
        {
            using var _ = Context.PushCurrent(Context.Empty.WithTenantId(tenantId));
            await Task.Yield();
            return Context.Current.TenantId;
        }
    }
}
