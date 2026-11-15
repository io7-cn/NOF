using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace NOF.Infrastructure.RabbitMQ.Tests;

public sealed class RabbitMQDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeConsumer_AcknowledgesOnlyAfterCompletion_AndNacksFailure(bool fail)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        var probe = new Probe { Fail = fail };
        builder.Services.AddSingleton(probe);
        builder.Services.AddScoped<Receive>();
        var operation = new OperationDescriptor<Work>(typeof(Receive));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>().AddOperation(
            new(typeof(Receive), typeof(Work), typeof(void)) { Descriptor = operation });
        var channel = new Mock<IChannel>();
        IAsyncBasicConsumer? consumer = null;
        channel.Setup(item => item.BasicConsumeAsync("work", false, It.IsAny<string>(), false, false,
                It.IsAny<IDictionary<string, object?>?>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, value, _) => consumer = value).ReturnsAsync("consumer");
        var connection = new Mock<IConnection>();
        connection.Setup(item => item.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync(channel.Object);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(item => item.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        builder.Services.AddSingleton(factory.Object);
        builder.Services.AddRabbitMQ().ConfigureRabbitMQ(rabbit => rabbit.MapQueue<Receive>("work", requeueOnFailure: false));
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            Assert.Null(host.Services.GetService<IOutbox>());
            Assert.Null(host.Services.GetService<IUnitOfWork>());
            var bytes = host.Services.GetRequiredService<IObjectSerializer>().Serialize(new Work(), typeof(Work));
            var delivery = consumer!.HandleBasicDeliverAsync("consumer", 7, false, "", "work",
                new BasicProperties { MessageId = "native/non-guid/id" }, bytes);
            await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            channel.Verify(item => item.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            channel.Verify(item => item.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            probe.Release.TrySetResult();
            await delivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("native/non-guid/id", probe.MessageId);
            channel.Verify(item => item.BasicAckAsync(7, false, It.IsAny<CancellationToken>()), fail ? Times.Never() : Times.Once());
            channel.Verify(item => item.BasicNackAsync(7, false, false, It.IsAny<CancellationToken>()), fail ? Times.Once() : Times.Never());
        }
        finally
        {
            probe.Release.TrySetResult();
            await host.StopAsync();
        }
    }

    public sealed record Work;
    public sealed class Probe
    {
        public bool Fail;
        public string? MessageId;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Receive(Probe probe) : OperationHandler<Work>
    {
        public override async Task HandleAsync(Work request, InvocationContext context, CancellationToken cancellationToken)
        {
            probe.MessageId = context.Context[NOFAbstractionConstants.Transport.Headers.MessageId]?.GetValue<string>();
            probe.Started.TrySetResult();
            await probe.Release.Task.WaitAsync(cancellationToken);
            if (probe.Fail)
            { throw new InvalidOperationException("Business failure"); }
        }
    }
}
