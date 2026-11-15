using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class InboxExecutionTests
{
    [Fact]
    public async Task Inbox_ExecutesOneWayUseCaseForAnOpaqueReceipt_WithoutAdapterHandlers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        builder.Services.AddTransactionalMessages();
        builder.Services.Configure<TransactionalMessageOptions>(options => options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10));
        var probe = new Probe();
        builder.Services.AddSingleton(probe);
        builder.Services.AddScoped<Handler>();
        var operation = new OperationDescriptor<Payload>(typeof(Handler));
        builder.Services.GetOrAddSingleton<InboxEndpointRegistry>().Add(new("custom-adapter:receipt", operation));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>().AddOperation(new(typeof(Handler), typeof(Payload), typeof(void))
        {
            Descriptor = operation
        });
        using var host = builder.Build();
        var id = Guid.NewGuid();
        var payload = host.Services.GetRequiredService<IObjectSerializer>().Serialize(new Payload([10, 20]), typeof(Payload));
        await host.Services.GetRequiredService<MessageInboxReceiver>().ReceiveAsync(id, payload, "custom-adapter:receipt", null, default);
        Assert.False(probe.Created);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            await probe.Started.Task.WaitAsync(timeout.Token);
            using (var pendingScope = host.Services.CreateScope())
            {
                var pending = await pendingScope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFInboxMessage>().SingleAsync(timeout.Token);
                Assert.Equal(InboxMessageStatus.Pending, pending.Status);
            }
            probe.Release.TrySetResult();
            while (true)
            {
                using var scope = host.Services.CreateScope();
                var message = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFInboxMessage>().SingleAsync(timeout.Token);
                if (message.Status == InboxMessageStatus.Processed)
                {
                    Assert.Equal(id, message.Id);
                    Assert.Equal("custom-adapter:receipt", message.Route);
                    break;
                }
                await Task.Delay(10, timeout.Token);
            }
            Assert.True(probe.Created);
            Assert.Equal(NOFAbstractionConstants.Tenant.HostId, probe.Tenant);
            Assert.Equal(new byte[] { 10, 20 }, probe.Payload);
        }
        finally
        {
            probe.Release.TrySetResult();
            await host.StopAsync();
        }
    }

    public sealed class Probe
    {
        public bool Created;
        public string? Tenant;
        public byte[]? Payload;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record Payload(byte[] Data);
    public sealed class Handler : OperationHandler<Payload>
    {
        private readonly Probe _probe;
        public Handler(Probe probe)
        {
            _probe = probe;
            probe.Created = true;
            probe.Tenant = Context.Current.TenantId;
        }
        public override async Task HandleAsync(Payload payload, InvocationContext context, CancellationToken cancellationToken)
        {
            _probe.Payload = payload.Data;
            _probe.Started.TrySetResult();
            await _probe.Release.Task.WaitAsync(cancellationToken);
        }
    }
}
