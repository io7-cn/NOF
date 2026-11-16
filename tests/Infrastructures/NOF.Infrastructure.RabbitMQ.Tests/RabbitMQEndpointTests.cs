using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests;

public sealed class RabbitMQEndpointTests
{
    [Fact]
    public void ClientOnlyRegistration_DoesNotEnablePersistenceOrBrokerConsumers()
    {
        var services = new ServiceCollection();
        services.AddRabbitMQ(factory => factory.HostName = "localhost");
        Assert.DoesNotContain(services, item => item.ImplementationType == typeof(RabbitMQConsumerHostedService));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(RabbitMQBuilder));
    }

    [Fact]
    public async Task RabbitMQEndpoints_AreNotVisibleToMemoryTransport()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddRabbitMQ(factory => factory.HostName = "localhost");
        builder.Services.ConfigureRabbitMQ(endpoints => endpoints.MapQueue("work", First));
        using var host = builder.Build();
        Assert.Null(host.Services.GetService<MemoryEndpointRegistry>());
        Assert.Null(host.Services.GetService<MemoryMessageClient>());
        Assert.Single(host.Services.GetRequiredService<RabbitMQBuilder>().Freeze().Endpoints);
        await Task.CompletedTask;
    }

    [Fact]
    public void SameRouteInDifferentAdapters_HasIndependentTargetsAndNoPersistence()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddMemoryMessaging(endpoints => endpoints.MapQueue("work", First));
        builder.Services.AddRabbitMQ(factory => factory.HostName = "localhost");
        builder.Services.ConfigureRabbitMQ(endpoints => endpoints.MapQueue("work", Second));
        using var host = builder.Build();
        Assert.Empty(host.Services.GetServices<IHostedService>().OfType<OutboxMessageBackgroundService>());
        Assert.Empty(host.Services.GetServices<IHostedService>().OfType<OutboxCleanupBackgroundService>());
        var memory = Assert.Single(host.Services.GetRequiredService<MemoryEndpointRegistry>().Endpoints);
        var rabbit = Assert.Single(host.Services.GetRequiredService<RabbitMQBuilder>().Freeze().Endpoints);
        Assert.Equal(First.Id, memory.Operation.Id);
        Assert.Equal(Second.Id, rabbit.Operation.Id);
        using var scope = host.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IOutbox>());
        Assert.Null(scope.ServiceProvider.GetService<IUnitOfWork>());
    }

    [Fact]
    public void DuplicateEndpointAndForgedFailureOperation_AreRejectedAtRegistration()
    {
        var services = new ServiceCollection();
        var endpoints = new RabbitMQBuilder(services);
        endpoints.MapQueue("work", First);
        Assert.Throws<InvalidOperationException>(() => endpoints.MapQueue("work", Second));
        Assert.Throws<ArgumentException>(() => endpoints.MapQueue<Input>("invalid",
            new(typeof(IWork), nameof(IWork.MayFail))));
    }

    private static OperationDescriptor<Input> First => new(typeof(IWork), nameof(IWork.First));
    private static OperationDescriptor<Input> Second => new(typeof(IWork), nameof(IWork.Second));
    public sealed record Input(string Value);
    public interface IWork : IApplicationService
    {
        void First(Input input);
        void Second(Input input);
        [FailureCase("Rejected")]
        Empty MayFail(Input input);
    }
}
