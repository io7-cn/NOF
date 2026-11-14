using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Text.Json;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class MessageInboxReceiverTests
{
    [Fact]
    public async Task Acceptance_BindsDeliveryTenantBeforeResolvingStore_AndRestoresCaller()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        string? constructorTenant = null;
        NOFInboxMessage? receipt = null;
        var store = new Mock<IInboxStore>();
        store.Setup(value => value.ReceiveAsync(It.IsAny<NOFInboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<NOFInboxMessage, CancellationToken>((message, _) => receipt = message).Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddScoped(_ => { constructorTenant = Context.Current.TenantId; return store.Object; });
        await using var provider = services.BuildServiceProvider();
        var receiver = new MessageInboxReceiver(provider, new JsonObjectSerializer(System.Text.Json.JsonSerializerOptions.CreateNOF()));
        await receiver.ReceiveAsync(Guid.NewGuid(), new byte[] { 1 }, "queue:work.v1",
            [new(NOFAbstractionConstants.Transport.Headers.TenantId, "delivery")], default);
        Assert.Equal("delivery", constructorTenant);
        Assert.Equal("queue:work.v1", receipt!.Route);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatabaseConflict_IsAcceptedOnlyWhenExactReceiptAlreadyExists(bool exists)
    {
        var id = Guid.NewGuid();
        var store = new Mock<IInboxStore>();
        var error = new DataException("storage failure");
        store.Setup(value => value.ReceiveAsync(It.IsAny<NOFInboxMessage>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);
        store.Setup(value => value.ExistsAsync(id, "subscription:audit", It.IsAny<CancellationToken>())).ReturnsAsync(exists);
        var services = new ServiceCollection().AddScoped(_ => store.Object);
        await using var provider = services.BuildServiceProvider();
        var receiver = new MessageInboxReceiver(provider, new JsonObjectSerializer(System.Text.Json.JsonSerializerOptions.CreateNOF()));
        var action = () => receiver.ReceiveAsync(id, new byte[] { 1 }, "subscription:audit", null, default);
        if (exists)
        {
            await action();
        }
        else
        {
            Assert.Same(error, await Assert.ThrowsAsync<DataException>(action));
        }
        store.Verify(value => value.ExistsAsync(id, "subscription:audit", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedErrorsAndCancellation_AreNeverAcknowledgedAsDuplicates(bool cancelled)
    {
        var store = new Mock<IInboxStore>();
        Exception error = cancelled ? new OperationCanceledException() : new InvalidOperationException("adapter bug");
        store.Setup(value => value.ReceiveAsync(It.IsAny<NOFInboxMessage>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);
        store.Setup(value => value.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await using var provider = new ServiceCollection().AddScoped(_ => store.Object).BuildServiceProvider();
        var receiver = new MessageInboxReceiver(provider, new JsonObjectSerializer(JsonSerializerOptions.CreateNOF()));
        Assert.Same(error, await Record.ExceptionAsync(() => receiver.ReceiveAsync(Guid.NewGuid(), new byte[] { 1 }, "queue", null, default)));
        store.Verify(value => value.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
