using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests.Services;

public class RabbitMQConsumerHostedServiceTests
{
    [Fact]
    public void QueueRegistrations_SelectOperationsWithoutAnyExchangeTopology()
    {
        var registry = new RabbitMQEndpointRegistry();
        var endpoints = new RabbitMQEndpointsBuilder(new ServiceCollection(), registry);
        var descriptor = new ServiceOperationDescriptor<TestCommand, Empty>(typeof(ITestService), nameof(ITestService.Apply));
        endpoints.MapQueue<TestCommand>("work.v1", descriptor);
        endpoints.MapQueue<TestCommand>("audit.v1", descriptor);
        Assert.Equal(new[] { "work.v1", "audit.v1" }, registry.Endpoints.Select(endpoint => endpoint.QueueName));
        Assert.Equal(new[] { "rabbitmq:queue:work.v1", "rabbitmq:queue:audit.v1" }, registry.Endpoints.Select(endpoint => endpoint.ConsumerId));
        Assert.Throws<InvalidOperationException>(() => endpoints.MapQueue<TestCommand>("late.v1", descriptor));
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenConsumerInitializationFails()
    {
        var manager = new RabbitMQConnectionManager(Options.Create(new RabbitMQOptions { HostName = "localhost" }));
        manager.Dispose();
        var registry = new RabbitMQEndpointRegistry();
        new RabbitMQEndpointsBuilder(new ServiceCollection(), registry).MapQueue<TestCommand>("work.v1",
            new ServiceOperationDescriptor<TestCommand, Empty>(typeof(ITestService), nameof(ITestService.Apply)));
        using var service = new RabbitMQConsumerHostedService(manager, Options.Create(new RabbitMQOptions()), registry,
            new MessageInboxReceiver(null!, new JsonObjectSerializer()), NullLogger<RabbitMQConsumerHostedService>.Instance);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public void NativeMessageIdentity_TakesPrecedenceOverEnvelopeHeaders()
    {
        var id = Guid.NewGuid();
        var headers = new[] { new KeyValuePair<string, string?>(NOFAbstractionConstants.Transport.Headers.MessageId, Guid.NewGuid().ToString()) };
        Assert.Equal(id, RabbitMQConsumerHostedService.ResolveMessageId(id.ToString(), headers));
    }

    [Fact]
    public void MissingNativeIdentity_UsesCaseInsensitiveEnvelopeHeader()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, RabbitMQConsumerHostedService.ResolveMessageId(null,
            [new(NOFAbstractionConstants.Transport.Headers.MessageId.ToUpperInvariant(), id.ToString())]));
    }

    [Fact]
    public void MissingIdentity_CreatesANewReceiptId()
    {
        Assert.NotEqual(Guid.Empty, RabbitMQConsumerHostedService.ResolveMessageId(null, null));
    }

    public interface ITestService : IApplicationService { Empty Apply(TestCommand request); }
    public sealed class TestCommand;
}
