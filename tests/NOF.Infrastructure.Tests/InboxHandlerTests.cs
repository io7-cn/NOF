using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class InboxHandlerTests
{
    [Fact]
    public async Task Inbox_CanProcessAnOpaqueAdapterKeyWithoutAnyOperationOrTopologyRegistry()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        builder.Services.AddInbox();
        builder.Services.Configure<TransactionalMessageOptions>(options => options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10));
        var probe = new Probe();
        builder.Services.AddKeyedScoped<IInboxHandler>("custom-adapter:receipt", (_, _) => new RawHandler(probe));
        using var host = builder.Build();
        var id = Guid.NewGuid();
        await host.Services.GetRequiredService<MessageInboxReceiver>().ReceiveAsync(id, new byte[] { 10, 20 }, "custom-adapter:receipt", null, default);
        Assert.False(probe.Created);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
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
            await host.StopAsync();
        }
    }

    public sealed class Probe
    {
        public bool Created;
        public string? Tenant;
        public byte[]? Payload;
    }

    public sealed class RawHandler : IInboxHandler
    {
        private readonly Probe _probe;
        public RawHandler(Probe probe)
        {
            _probe = probe;
            probe.Created = true;
            probe.Tenant = Context.Current.TenantId;
        }
        public Task<object?> HandleAsync(ReadOnlyMemory<byte> payload, IEnumerable<KeyValuePair<string, string?>>? headers, CancellationToken cancellationToken)
        {
            _probe.Payload = payload.ToArray();
            return Task.FromResult<object?>(Empty.Instance);
        }
    }
}
