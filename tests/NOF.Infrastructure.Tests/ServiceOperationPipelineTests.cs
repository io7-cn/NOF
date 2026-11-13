using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.Tests;

public class ServiceOperationPipelineTests
{
    [Fact]
    public async Task RejectedOperations_DoNotConstructHandlers()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ProbeOperation>(_ => throw new InvalidOperationException("Handler constructed"));
        services.AddScoped<IInvocationFilter, Deny>();
        using var provider = services.BuildServiceProvider();
        var result = await Execute(provider);
        Assert.Equal("403", Assert.IsType<ServiceRejected>(result.Execution.Value).Failure.Code);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task NormalizedContext_IsBoundBeforeDependencyResolution_AndRestoredAfterAwait()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ProbeOperation>();
        services.AddScoped<IInvocationFilter, Normalize>();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        var first = await Execute(firstScope.ServiceProvider);
        var again = await Execute(firstScope.ServiceProvider);
        var other = await Execute(otherScope.ServiceProvider);
        var reply = first.Execution.Require<Reply>();
        Assert.Equal("normalized", reply.Tenant);
        Assert.Equal("normalized", reply.ConstructorTenant);
        Assert.Equal(reply.HandlerId, again.Execution.Require<Reply>().HandlerId);
        Assert.NotEqual(reply.HandlerId, other.Execution.Require<Reply>().HandlerId);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task Cancellation_DoesNotConstructHandlers()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ProbeOperation>(_ => throw new InvalidOperationException("Handler constructed"));
        using var provider = services.BuildServiceProvider();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Execute(provider, source.Token));
    }

    [Fact]
    public async Task Exceptions_PropagateAndRestoreAmbientContext()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ThrowingOperation>();
        using var provider = services.BuildServiceProvider();
        var pipeline = new ApplicationServiceExecutionAdapter(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ExecuteAsync(new Query(),
            typeof(ThrowingOperation), typeof(Reply), typeof(IProbeService), nameof(IProbeService.Read), null, default));
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task NullableCompletion_IsSeparateFromUninitializedExecution()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<NullableOperation>();
        using var provider = services.BuildServiceProvider();
        var result = await new ApplicationServiceExecutionAdapter(provider).ExecuteAsync(new Query(), typeof(NullableOperation),
            typeof(Reply), typeof(IProbeService), nameof(IProbeService.Find), null, default);
        Assert.IsType<ServiceCompleted>(result.Execution.Value);
        Assert.Null(result.Execution.Require<Reply?>(allowNull: true));
        Assert.Null(result.Rejection);
    }

    private static async Task<RequestInboundContext> Execute(IServiceProvider provider, CancellationToken cancellationToken = default)
        => await new ApplicationServiceExecutionAdapter(provider).ExecuteAsync(new Query(), typeof(ProbeOperation), typeof(Reply),
            typeof(IProbeService), nameof(IProbeService.Read), null, cancellationToken);

    public sealed record Query;
    public sealed record Reply(Guid HandlerId, string Tenant, string ConstructorTenant);
    public interface IProbeService : IApplicationService
    {
        Reply Read(Query request);
        Reply? Find(Query request);
    }

    public sealed class ProbeOperation : ServiceOperation<Query, Reply>
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly string _constructorTenant = Context.Current.TenantId;

        public override async Task<Reply> HandleAsync(Query request, Context context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Assert.Same(context, Context.Current);
            return new Reply(_id, context.TenantId, _constructorTenant);
        }
    }

    public sealed class ThrowingOperation : ServiceOperation<Query, Reply>
    {
        public override async Task<Reply> HandleAsync(Query request, Context context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("Expected");
        }
    }

    public sealed class NullableOperation : ServiceOperation<Query, Reply?>
    {
        public override bool AllowsNullResponse => true;
        public override Task<Reply?> HandleAsync(Query request, Context context, CancellationToken cancellationToken)
            => Task.FromResult<Reply?>(null);
    }

    public sealed class Deny : IInvocationFilter
    {
        public ValueTask InvokeAsync(InvocationContext context, object request, InvocationDelegate next, CancellationToken cancellationToken)
        {
            context.Reject(new Failure("403", "Forbidden"));
            return ValueTask.CompletedTask;
        }
    }

    public sealed class Normalize : IInvocationFilter
    {
        public ValueTask InvokeAsync(InvocationContext context, object request, InvocationDelegate next, CancellationToken cancellationToken)
            => next((RequestInboundContext)context.WithTenantId("normalized"), request, cancellationToken);
    }
}
