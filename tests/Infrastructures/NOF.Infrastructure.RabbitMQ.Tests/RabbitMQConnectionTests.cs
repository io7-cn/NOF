using Moq;
using RabbitMQ.Client;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests;

public sealed class RabbitMQConnectionTests
{
    [Fact]
    public async Task ConcurrentCallers_ShareOneAttempt_AndCallerCancellationDoesNotCancelIt()
    {
        var pending = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>())).Returns(pending.Task);
        var native = new Mock<IConnection>();
        var owner = new RabbitMQConnection(factory.Object);
        using var caller = new CancellationTokenSource();
        var canceled = owner.GetAsync(caller.Token);
        var other = owner.GetAsync();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(other.IsCompleted);
        pending.SetResult(native.Object);
        Assert.Same(native.Object, await other);
        Assert.Same(native.Object, await owner.GetAsync());
        factory.Verify(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        await owner.DisposeAsync();
        owner.Dispose();
        native.Verify(item => item.DisposeAsync(), Times.Once);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.GetAsync());
    }

    [Fact]
    public async Task FailedInitialConnect_CanBeRetriedWithoutReplacingAnEstablishedRecoveringConnection()
    {
        var factory = new Mock<IConnectionFactory>();
        var native = new Mock<IConnection>();
        native.SetupGet(item => item.IsOpen).Returns(false);
        factory.SetupSequence(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline"))
            .ReturnsAsync(native.Object);
        await using var owner = new RabbitMQConnection(factory.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.GetAsync());
        Assert.Same(native.Object, await owner.GetAsync());
        Assert.Same(native.Object, await owner.GetAsync());
        factory.Verify(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Disposal_CancelsAnInFlightAttempt_WithoutOpeningAnUnusedConnection()
    {
        var factory = new Mock<IConnectionFactory>();
        var pending = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Setup(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) =>
        {
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        });
        var unused = new RabbitMQConnection(factory.Object);
        await unused.DisposeAsync();
        factory.VerifyNoOtherCalls();
        var owner = new RabbitMQConnection(factory.Object);
        var attempt = owner.GetAsync();
        await owner.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        factory.Verify(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
