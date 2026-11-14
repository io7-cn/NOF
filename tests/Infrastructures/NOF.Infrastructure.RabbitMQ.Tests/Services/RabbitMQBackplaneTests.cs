using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests.Services;

public sealed class RabbitMQBackplaneTests
{
    [Fact]
    public void BuildExchangeName_ShouldUseDedicatedBackplanePrefix()
    {
        var exchangeName = RabbitMQBackplane.BuildExchangeName("chat-stream");

        Assert.Equal("nof.backplane.chat-stream", exchangeName);
    }

    [Fact]
    public void AddRabbitMQBackplane_OnServices_ShouldRegisterBackplaneWithoutConsumerHostedService()
    {
        var services = new ServiceCollection();

        services.AddRabbitMQ(factory => factory.Uri = new Uri("amqp://guest:guest@localhost:5672/"));
        services.AddRabbitMQBackplane();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IBackplane) &&
                          descriptor.ImplementationType == typeof(RabbitMQBackplane));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(RabbitMQConnection) &&
                          descriptor.ImplementationType == typeof(RabbitMQConnection));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                          descriptor.ImplementationType == typeof(RabbitMQConsumerHostedService));
    }

}
