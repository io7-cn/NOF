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
        var endpoints = new RabbitMQBuilder(new ServiceCollection());
        var descriptor = new ServiceOperationDescriptor<TestCommand, Empty>(typeof(ITestService), nameof(ITestService.Apply));
        endpoints.MapQueue("work.v1", descriptor);
        endpoints.MapQueue("audit.v1", descriptor);
        Assert.Equal(new[] { "work.v1", "audit.v1" }, endpoints.Freeze().Endpoints.Select(endpoint => endpoint.QueueName));
        Assert.Equal(new[] { "rabbitmq:queue:work.v1", "rabbitmq:queue:audit.v1" }, endpoints.Freeze().Endpoints.Select(endpoint => endpoint.ConsumerId));
        Assert.Throws<InvalidOperationException>(() => endpoints.MapQueue("late.v1", descriptor));
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenConsumerInitializationFails()
    {
        var manager = new RabbitMQConnection(new global::RabbitMQ.Client.ConnectionFactory { HostName = "localhost" });
        manager.Dispose();
        var endpoints = new RabbitMQBuilder(new ServiceCollection());
        endpoints.MapQueue("work.v1",
            new ServiceOperationDescriptor<TestCommand, Empty>(typeof(ITestService), nameof(ITestService.Apply)));
        using var service = new RabbitMQConsumerHostedService(manager, endpoints,
            new ServiceCollection().BuildServiceProvider(), NullLogger<RabbitMQConsumerHostedService>.Instance);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(CancellationToken.None));
    }

    public interface ITestService : IApplicationService { Empty Apply(TestCommand request); }
    public sealed class TestCommand;
}
