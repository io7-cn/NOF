using Microsoft.Extensions.DependencyInjection;
using Moq;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public sealed class MemoryMessageMetadataTests
{
    [Theory]
    [InlineData(true, "explicit")]
    [InlineData(false, "ambient")]
    public async Task DirectSend_SnapshotsJsonContextAndTenant_WithoutDeliveryHelpers(bool explicitTenant, string expected)
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("ambient"));
        NOFInboxMessage? captured = null;
        var store = new Mock<IInboxStore>();
        store.Setup(value => value.ReceiveAsync(It.IsAny<NOFInboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<NOFInboxMessage, CancellationToken>((message, _) => captured = message).Returns(Task.CompletedTask);
        using var provider = Services(store.Object).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var context = Context.Empty.WithItem("correlation", "original").WithItem("count", 7).WithItem("enabled", true).WithItem("empty", null);
        if (explicitTenant)
        {
            context = context.WithTenantId("explicit");
        }
        await scope.ServiceProvider.GetRequiredService<MemoryMessageClient>().SendAsync("work", new Message(), context);
        var headers = provider.GetRequiredService<IObjectSerializer>().Deserialize<Dictionary<string, string?>>(captured!.Headers)!;
        Assert.Equal(expected, headers[NOFAbstractionConstants.Transport.Headers.TenantId]);
        Assert.Equal(captured.Id.ToString(), headers[NOFAbstractionConstants.Transport.Headers.MessageId]);
        var snapshot = JsonNode.Parse(headers[NOFAbstractionConstants.Transport.Headers.Context]!)!.AsObject();
        Assert.Equal("original", snapshot["correlation"]!.GetValue<string>());
        Assert.Equal(7, snapshot["count"]!.GetValue<int>());
        Assert.True(snapshot["enabled"]!.GetValue<bool>());
        Assert.True(snapshot.ContainsKey("empty"));
        Assert.Null(snapshot["empty"]);
    }

    [Fact]
    public async Task DirectSend_BindsProducerTrace_AndPropagatesAcceptanceFailure()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NOFHostingConstants.Outbound.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("caller").Start();
        var expected = new InvalidOperationException("acceptance failed");
        var store = new Mock<IInboxStore>();
        store.Setup(value => value.ReceiveAsync(It.IsAny<NOFInboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.Equal(ActivityKind.Producer, Activity.Current!.Kind);
                Assert.Equal(parent.Id, Activity.Current.ParentId);
            }).ThrowsAsync(expected);
        using var provider = Services(store.Object).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<MemoryMessageClient>()
            .SendAsync("work", new Message(), Context.Empty));
        Assert.Same(expected, actual);
        Assert.Same(parent, Activity.Current);
    }

    [Fact]
    public async Task Cancellation_DoesNotAcceptAReceipt()
    {
        var store = new Mock<IInboxStore>();
        using var provider = Services(store.Object).BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<MemoryMessageClient>()
            .SendAsync("work", new Message(), Context.Empty, cancellation.Token));
        store.Verify(value => value.ReceiveAsync(It.IsAny<NOFInboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ServiceCollection Services(IInboxStore store)
    {
        var services = new ServiceCollection();
        services.AddMemoryMessaging(endpoints => endpoints.MapQueue("work", new OperationDescriptor<Message>(typeof(IService), nameof(IService.Run))));
        services.AddSingleton(store);
        return services;
    }

    public sealed record Message;
    public interface IService : IApplicationService
    {
        void Run(Message request);
    }
}
