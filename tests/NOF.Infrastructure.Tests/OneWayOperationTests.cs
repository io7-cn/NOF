using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class OneWayOperationTests
{
    [Fact]
    public async Task OneWayExecution_HasNoBusinessResult_AndReplyInvokerCannotAcceptIt()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddScoped<Handler>();
        var operation = new OperationDescriptor<Message>(typeof(IService), nameof(IService.Send));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>().Add(new(typeof(IService), typeof(object))
        {
            HandlerMappings = new Dictionary<string, OperationMapping>
            {
                [nameof(IService.Send)] = new(typeof(Handler), typeof(Message), typeof(void)) { Descriptor = operation }
            }
        });
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<OperationExecutor>()
            .ExecuteInScopeAsync(operation, new Message(), Context.Empty, InvocationOrigin.Message);
        Assert.Null(result);
        Assert.True(operation.IsOneWay);
        Assert.False(operation.AllowsNullResponse);
        Assert.Empty(operation.ResultCases);
        var plan = scope.ServiceProvider.GetRequiredService<IInvocationPlanProvider>().Describe(operation, InvocationOrigin.Message);
        Assert.Empty(plan.BusinessCases);
        Assert.Throws<InvalidOperationException>(() => plan.Validate(Empty.Instance));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IOperationExecutor>()
            .ExecuteAsync(operation, new Message(), Context.Empty));
    }

    [Fact]
    public void Inbox_RejectsReplyContracts_AndKeepsReceiptIdentityIndependentOfTransportTopology()
    {
        var registry = new InboxEndpointRegistry();
        var operation = new OperationDescriptor<Message>(typeof(IService), nameof(IService.Send));
        var reply = new OperationDescriptor<Message, Empty>(typeof(IService), nameof(IService.Reply));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new("opaque:reply", reply)));
        registry.Add(new("opaque:first", operation));
        registry.Add(new("opaque:second", operation));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new("opaque:first", operation)));
        Assert.Same(operation, registry.GetOperation("opaque:first"));
        Assert.Same(operation, registry.GetOperation("opaque:second"));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new("opaque:late", operation)));
        Assert.Throws<InvalidOperationException>(() => registry.GetOperation("missing"));
    }

    public sealed record Message;
    public interface IService : IApplicationService
    {
        void Send(Message request);
        Empty Reply(Message request);
    }
    public sealed class Handler : OperationHandler<Message>
    {
        public override Task HandleAsync(Message request, InvocationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
