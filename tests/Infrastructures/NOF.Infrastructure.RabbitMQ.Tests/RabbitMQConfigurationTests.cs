using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NOF.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests;

public sealed class RabbitMQConfigurationTests
{
    [Fact]
    public void RepeatedDeclarations_ComposeWithOneLifecycle_AndFreezeTogether()
    {
        var services = new ServiceCollection();
        services.ConfigureRabbitMQ(rabbit => rabbit.Topology((_, _) => Task.CompletedTask));
        services.ConfigureRabbitMQ(rabbit => rabbit.Topology((_, _) => Task.CompletedTask)
            .MapQueue<RabbitMQEndpointTests.Input>("first", new(typeof(RabbitMQEndpointTests.IWork), nameof(RabbitMQEndpointTests.IWork.First))));
        services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<RabbitMQEndpointTests.Input>("second",
            new(typeof(RabbitMQEndpointTests.IWork), nameof(RabbitMQEndpointTests.IWork.Second))));
        services.AddRabbitMQ(factory => factory.Uri = new Uri("amqps://user:password@broker.test:5671/demo"));
        services.AddRabbitMQ(factory => factory.ClientProvidedName = "native-settings");
        using var provider = services.BuildServiceProvider();
        var factory = Assert.IsType<ConnectionFactory>(provider.GetRequiredService<IConnectionFactory>());
        Assert.True(factory.Ssl.Enabled);
        Assert.Equal("demo", factory.VirtualHost);
        Assert.Equal("native-settings", factory.ClientProvidedName);
        var configuration = provider.GetRequiredService<RabbitMQConfiguration>();
        var snapshot = configuration.Freeze();
        Assert.Equal(2, snapshot.Topologies.Count);
        Assert.Equal(new[] { "first", "second" }, snapshot.Endpoints.Select(item => item.QueueName));
        Assert.Single(services, item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(RabbitMQConsumerHostedService));
        Assert.Throws<InvalidOperationException>(() => services.ConfigureRabbitMQ(rabbit => rabbit.Topology((_, _) => Task.CompletedTask)));
        Assert.Throws<InvalidOperationException>(() => services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<RabbitMQEndpointTests.Input>("late",
            new(typeof(RabbitMQEndpointTests.IWork), nameof(RabbitMQEndpointTests.IWork.First)))));
    }

    [Fact]
    public void ConfigurationAfterBuild_IsRejectedBeforeItCanMutateTheHostSnapshot()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddRabbitMQ();
        RabbitMQBuilder? captured = null;
        builder.Services.ConfigureRabbitMQ(rabbit => captured = rabbit.Topology((_, _) => Task.CompletedTask));
        using var host = builder.Build();
        var invoked = false;
        Assert.Throws<InvalidOperationException>(() => builder.Services.ConfigureRabbitMQ(_ => invoked = true));
        Assert.Throws<InvalidOperationException>(() => captured!.Topology((_, _) => Task.CompletedTask));
        Assert.False(invoked);
        Assert.Single(host.Services.GetRequiredService<RabbitMQConfiguration>().Freeze().Topologies);
    }

    [Fact]
    public async Task TopologyOnlyHost_RunsOrderedCallbacksWithoutEnablingInbox()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        var native = new Mock<IConnection>();
        var channel = new Mock<IChannel>();
        native.Setup(item => item.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync(channel.Object);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(native.Object);
        builder.Services.AddSingleton(factory.Object);
        builder.Services.AddRabbitMQ();
        var calls = new List<int>();
        builder.Services.ConfigureRabbitMQ(rabbit => rabbit.Topology(async (value, ct) =>
        {
            Assert.Same(channel.Object, value);
            calls.Add(1);
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
        }));
        builder.Services.ConfigureRabbitMQ(rabbit => rabbit.Topology((value, _) =>
        {
            Assert.Same(channel.Object, value);
            calls.Add(2);
            return Task.CompletedTask;
        }));
        using var host = builder.Build();
        await host.StartAsync();
        Assert.Equal(new[] { 1, 2 }, calls);
        using var scope = host.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IInboxStore>());
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), item => item is InboxMessageBackgroundService or InboxCleanupBackgroundService);
        await host.StopAsync();
    }

    [Fact]
    public async Task FailedTopology_AbortsStartupBeforeLaterCallbacksOrConsumers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        var native = new Mock<IConnection>();
        var channel = new Mock<IChannel>();
        native.Setup(item => item.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync(channel.Object);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(native.Object);
        builder.Services.AddSingleton(factory.Object);
        builder.Services.AddRabbitMQ();
        var later = false;
        var error = new InvalidOperationException("topology failed");
        builder.Services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<RabbitMQEndpointTests.Input>("work",
            new(typeof(RabbitMQEndpointTests.IWork), nameof(RabbitMQEndpointTests.IWork.First))));
        builder.Services.ConfigureRabbitMQ(rabbit => rabbit.Topology((_, _) => Task.FromException(error))
            .Topology((_, _) =>
            {
                later = true;
                return Task.CompletedTask;
            }));
        using var host = builder.Build();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync()));
        Assert.False(later);
        Assert.DoesNotContain(channel.Invocations, item => item.Method.Name == nameof(IChannel.BasicConsumeAsync));
    }
}
