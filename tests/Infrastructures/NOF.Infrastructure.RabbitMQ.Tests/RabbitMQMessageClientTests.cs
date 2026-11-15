using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NOF.Abstraction;
using NOF.Hosting;
using RabbitMQ.Client;
using System.Text.Json;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests;

public sealed class RabbitMQMessageClientTests
{
    [Fact]
    public async Task ClientOnlyRegistration_PublishesConfirmedJsonWithoutTopologyOrWorkers()
    {
        var channel = new Mock<IChannel>();
        var native = new Mock<IConnection>();
        native.Setup(value => value.CreateChannelAsync(It.Is<CreateChannelOptions>(options =>
            options.PublisherConfirmationsEnabled && options.PublisherConfirmationTrackingEnabled), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel.Object);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(value => value.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(native.Object);
        var services = new ServiceCollection();
        services.AddSingleton(factory.Object);
        services.AddRabbitMQ().AddRabbitMQ();
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<RabbitMQMessageClient>();
        Assert.Same(client, provider.GetRequiredService<RabbitMQMessageClient>());
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Null(provider.GetService<NOF.Application.IOutbox>());
        var headers = new Dictionary<string, string?> { [NOFAbstractionConstants.Transport.Headers.MessageId] = "delivery-1", ["custom"] = "value" };
        await client.PublishAsync("orders", "order.created", "hello", headers);
        var publish = Assert.Single(channel.Invocations, call => call.Method.Name == nameof(IChannel.BasicPublishAsync));
        Assert.Equal("orders", publish.Arguments[0]);
        Assert.Equal("order.created", publish.Arguments[1]);
        Assert.Equal(true, publish.Arguments[2]);
        var properties = Assert.IsType<BasicProperties>(publish.Arguments[3]);
        Assert.True(properties.Persistent);
        Assert.Equal("application/json", properties.ContentType);
        Assert.Equal("delivery-1", properties.MessageId);
        Assert.Equal("value", properties.Headers!["custom"]);
        Assert.Equal("hello", JsonSerializer.Deserialize<string>(((ReadOnlyMemory<byte>)publish.Arguments[4]).Span));
        Assert.DoesNotContain(channel.Invocations, call => call.Method.Name.Contains("Declare") || call.Method.Name.Contains("Bind"));
    }
}
