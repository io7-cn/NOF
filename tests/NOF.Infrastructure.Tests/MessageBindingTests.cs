using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class MessageBindingTests
{
    [Fact]
    public void EmptySuccessWithDeclaredFailures_CannotBeSubmittedOrBoundAsEmpty()
    {
        var registry = new MessageBindingRegistry();
        var bindings = new MessageBindingsBuilder(registry);
        var reference = new ServiceOperationReference<Message>(typeof(IService), nameof(IService.MayFail));
        Assert.False(ServiceOperationInspector.CanSubmit(reference));
        // Even a manually forged success-only descriptor must not bypass the Contract declaration.
        var descriptor = new ServiceOperationDescriptor<Message, Empty>(typeof(IService), nameof(IService.MayFail));
        Assert.Throws<InvalidOperationException>(() => bindings.Queue<Message>("invalid.v1").To(descriptor));
        Assert.Throws<InvalidOperationException>(() => bindings.Subscription<Message>("facts.v1", "invalid").To(descriptor));
    }
    private static ServiceOperationDescriptor<Message, Empty> First => new(typeof(IService), nameof(IService.First));
    private static ServiceOperationDescriptor<Message, Empty> Second => new(typeof(IService), nameof(IService.Second));

    [Fact]
    public void SharedInput_DifferentOperationsAndSubscriptions_HaveIndependentIdentities()
    {
        var registry = new MessageBindingRegistry();
        var bindings = new MessageBindingsBuilder(registry);
        bindings.Queue<Message>("first.v1").To(First);
        bindings.Queue<Message>("second.v1").To(Second);
        bindings.Subscription<Message>("facts.v1", "projection").To(First);
        bindings.Subscription<Message>("facts.v1", "audit").To(Second);
        Assert.Equal("first.v1", registry.GetConsumer("queue:first.v1").Route);
        Assert.Equal("second.v1", registry.GetConsumer("queue:second.v1").Route);
        Assert.Equal(First.Id, registry.GetConsumer("subscription:projection").Operation.Id);
        Assert.Equal(Second.Id, registry.GetConsumer("subscription:audit").Operation.Id);
        Assert.Throws<InvalidOperationException>(() => bindings.Queue<Message>("late.v1").To(First));
    }

    [Fact]
    public void OneOperation_CanReceiveMultipleExplicitInboundQueues()
    {
        var registry = new MessageBindingRegistry();
        var bindings = new MessageBindingsBuilder(registry);
        bindings.Queue<Message>("first.v1").To(First);
        bindings.Queue<Message>("second.v1").To(First);
        Assert.Equal(2, registry.Bindings.Count);
        Assert.All(registry.Bindings, binding => Assert.Equal(First.Id, binding.Operation.Id));
        Assert.Null(typeof(MessageBindingsBuilder).GetMethod("RouteToQueue"));
        Assert.Null(typeof(MessageBindingsBuilder).GetMethod("Topic"));
    }

    [Fact]
    public void AmbiguousConsumerIds_AreRejected()
    {
        var registry = new MessageBindingRegistry();
        var bindings = new MessageBindingsBuilder(registry);
        bindings.Subscription<Message>("facts.v1", "same").To(First);
        bindings.Subscription<Message>("facts.v1", "same").To(Second);
        Assert.Throws<InvalidOperationException>(() => registry.Bindings);
    }

    [Fact]
    public void HostRegistries_AreIsolated()
    {
        var first = new ServiceCollection().AddMessageBindings(bindings => bindings.Queue<Message>("one.v1").To(First));
        var second = new ServiceCollection().AddMessageBindings(bindings => bindings.Queue<Message>("two.v1").To(First));
        using var one = first.BuildServiceProvider();
        using var two = second.BuildServiceProvider();
        Assert.Equal("one.v1", one.GetRequiredService<MessageBindingRegistry>().Bindings.Single().Route);
        Assert.Equal("two.v1", two.GetRequiredService<MessageBindingRegistry>().Bindings.Single().Route);
    }

    [Theory]
    [InlineData(InvocationOrigin.Local)]
    [InlineData(InvocationOrigin.Http)]
    [InlineData(InvocationOrigin.Message)]
    public async Task OperationPolicies_ApplyToEveryOrigin_BeforeHandlerConstruction(InvocationOrigin origin)
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddInvocationFilter<Reject>();
        services.AddScoped<Handler>(_ => throw new InvalidOperationException("Constructed a rejected handler"));
        using var provider = services.BuildServiceProvider();
        var registry = new ApplicationServiceRegistry();
        registry.Add(new(typeof(IService), typeof(object))
        {
            HandlerMappings = new Dictionary<string, ServiceOperationMapping> { [nameof(IService.First)] = new(typeof(Handler), typeof(Message), typeof(Empty)) }
        });
        var executor = new OperationExecutor(provider, new ApplicationServiceInvocationResolver(provider, registry));
        var result = await executor.ExecuteAsync(First, new Message(), Context.Empty, origin);
        Assert.Equal("403", Assert.IsType<Denied>(result).Title);
    }

    public interface IService : IApplicationService
    {
        Empty First(Message input);
        Empty Second(Message input);
        [FailureCase("Rejected")]
        Empty MayFail(Message input);
    }
    public sealed record Message;
    public sealed record Denied(string Title) : IFailure { public string Detail => "Denied"; }
    public sealed class Reject : IInvocationFilter
    {
        public static IReadOnlyList<InvocationFailureCase> FailureCases => [new("deny", typeof(Denied), typeof(Denied), "403", value => value, _ => "Denied")];
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<object?>(new Denied("403"));
        }
    }
    public sealed class Handler : ServiceOperation<Message, Empty>
    {
        public override Task<Empty> HandleAsync(Message input, InvocationContext context, CancellationToken cancellationToken) => Task.FromResult(Empty.Instance);
    }
}
