using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests.Services;

public class RabbitMQConsumerHostedServiceTests
{
    [Fact]
    public void ConsumerRegistrations_UseExplicitStableRoutesAndIndependentSubscriptions()
    {
        var registry = new MessageBindingRegistry();
        var bindings = new MessageBindingsBuilder(registry);
        var descriptor = new ServiceOperationDescriptor<TestCommand, Empty>(typeof(ITestService), nameof(ITestService.Apply));
        bindings.Queue<TestCommand>("work.v1").To(descriptor);
        bindings.Subscription<TestCommand>("facts.v1", "projection").To(descriptor);
        bindings.Subscription<TestCommand>("facts.v1", "audit").To(descriptor);
        var queue = Assert.Single(RabbitMQConsumerHostedService.BuildCommandConsumerRegistrations(registry.Bindings));
        Assert.Equal("work.v1", queue.QueueName);
        Assert.Equal("queue:work.v1", queue.ConsumerId);
        var subscriptions = RabbitMQConsumerHostedService.BuildNotificationConsumerRegistrations(registry.Bindings);
        Assert.Equal(["subscription:projection", "subscription:audit"], subscriptions.Select(binding => binding.QueueName));
        Assert.All(subscriptions, binding => Assert.Equal(["facts.v1"], binding.NotificationTypes));
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenConsumerInitializationFails()
    {
        var manager = new RabbitMQConnectionManager(Options.Create(new RabbitMQOptions { HostName = "localhost" }));
        manager.Dispose();
        var registry = new MessageBindingRegistry();
        new MessageBindingsBuilder(registry).Queue<TestCommand>("work.v1")
            .To(new ServiceOperationDescriptor<TestCommand, Empty>(typeof(ITestService), nameof(ITestService.Apply)));
        using var service = new RabbitMQConsumerHostedService(manager, Options.Create(new RabbitMQOptions()), registry,
            new MessageInboxReceiver(null!, new JsonObjectSerializer()), NullLogger<RabbitMQConsumerHostedService>.Instance);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(CancellationToken.None));
    }

    private interface ITestService : IApplicationService { Empty Apply(TestCommand request); }

    [Fact]
    public void ShouldRequeue_ShouldUseOptionsForUnexpectedFailures()
    {
        var requeue = RabbitMQConsumerHostedService.ShouldRequeue(
            new InvalidOperationException("database unavailable"),
            new RabbitMQOptions());

        Assert.True(requeue);
    }

    [Fact]
    public void ShouldRequeue_ShouldRespectConfiguredConsumerFailurePolicy()
    {
        var requeue = RabbitMQConsumerHostedService.ShouldRequeue(
            new InvalidOperationException("database unavailable"),
            new RabbitMQOptions { RequeueOnConsumerFailure = false });

        Assert.False(requeue);
    }

    [Fact]
    public void ShouldRequeue_ShouldUseMessageExceptionPolicyForPoisonMessages()
    {
        var requeue = RabbitMQConsumerHostedService.ShouldRequeue(
            new RabbitMQConsumerMessageException("missing message type", requeue: false),
            new RabbitMQOptions());

        Assert.False(requeue);
    }

    [Fact]
    public void RabbitMQTopology_ShouldUseNamespacedInfrastructureNames()
    {
        Assert.StartsWith("nof.io-vii.com.", RabbitMQTopology.UnroutableExchangeName, StringComparison.Ordinal);
        Assert.StartsWith("nof.io-vii.com.", RabbitMQTopology.UnroutableQueueName, StringComparison.Ordinal);
        Assert.StartsWith("nof.io-vii.com.", RabbitMQTopology.DeadLetterExchangeName, StringComparison.Ordinal);
        Assert.StartsWith("nof.io-vii.com.", RabbitMQTopology.DeadLetterQueueName, StringComparison.Ordinal);
    }

    [Fact]
    public void RabbitMQTopology_ShouldConnectBusinessResourcesToFailureExchanges()
    {
        var exchangeArguments = RabbitMQTopology.BuildBusinessExchangeArguments();
        var queueArguments = RabbitMQTopology.BuildBusinessQueueArguments();

        Assert.Equal(
            RabbitMQTopology.UnroutableExchangeName,
            exchangeArguments["alternate-exchange"]);
        Assert.Equal(
            RabbitMQTopology.DeadLetterExchangeName,
            queueArguments["x-dead-letter-exchange"]);
    }

    [Fact]
    public void RabbitMQTopology_ShouldAddOriginalRouteHeadersForReplay()
    {
        var headers = new Dictionary<string, object?>();

        RabbitMQTopology.AddOriginalRouteHeaders(headers, "App.OrderCreated", string.Empty);

        Assert.Equal("App.OrderCreated", headers[RabbitMQTopology.OriginalExchangeHeader]);
        Assert.Equal(string.Empty, headers[RabbitMQTopology.OriginalRoutingKeyHeader]);
    }

    private sealed class TestCommand;

    private sealed class TestCommandHandler;

    private sealed class SecondTestCommandHandler;

    private sealed class TestNotification;

    private sealed class FirstTestNotificationHandler;

    private sealed class SecondTestNotificationHandler;

}
