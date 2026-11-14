using NOF.Sample.Application;
using NOF.Sample.Notifications;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class MessagingBoundariesTests
{
    [Fact]
    public void SharedDeclarationsHaveNoTransportDependency_AndReceiverOperationsBelongToTheHost()
    {
        Assert.Same(typeof(IMessageDemoService).Assembly, typeof(RabbitMQDemoRoutes).Assembly);
        Assert.Same(typeof(MessageDispatchService).Assembly, typeof(IMessageClient).Assembly);
        Assert.Equal("NOF.Sample.Notifications", typeof(MessageDemoService).Assembly.GetName().Name);
        Assert.DoesNotContain(typeof(IMessageClient).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name == "RabbitMQ.Client" || reference.Name!.StartsWith("NOF.Infrastructure", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(RabbitMQDemoRoutes).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name == "RabbitMQ.Client" || reference.Name!.StartsWith("NOF.Infrastructure", StringComparison.Ordinal));
    }
}
