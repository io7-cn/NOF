using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class InvocationPipelineTests
{
    [Fact]
    public void FilterRegistration_AloneDoesNotSelectOrRunAnEngine()
    {
        var services = new ServiceCollection().AddInvocationFilter<MustNotRun>();
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IInvocationPipeline>());
        Assert.Single(provider.GetServices<InvocationFilterRegistration>());
    }

    [Fact]
    public async Task HostExecutor_ControlsWhetherRegisteredFiltersRun()
    {
        var services = new ServiceCollection().AddInvocationFilter<MustNotRun>();
        services.AddScoped<IInvocationPipeline, HostPipeline>();
        services.UseInMemoryInvocationPipeline();
        using var provider = services.BuildServiceProvider();
        var context = CreateContext();
        var executor = provider.GetRequiredService<IInvocationPipeline>();
        await executor.ExecuteAsync(context, new object(), (current, _, _) =>
        {
            current.SetResponse(current.TenantId);
            return ValueTask.CompletedTask;
        }, default);
        Assert.IsType<HostPipeline>(executor);
        Assert.Equal("host", context.Execution.Require<string>());
    }

    [Fact]
    public async Task InMemoryEngine_UsesStableOrderAndLazyScopedFilters_WithRestoredAmbientContext()
    {
        using var caller = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddInvocationFilter<Normalize>(-100);
        services.AddInvocationFilter<Probe>();
        services.AddInvocationFilter<Probe>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = CreateContext();
        await scope.ServiceProvider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(context, new object(), async (current, _, _) =>
        {
            await Task.Yield();
            Assert.Same(current.Context, Context.Current);
            current.SetResponse(current.TenantId);
        }, default);
        Assert.Equal("normalized", context.Execution.Require<string>());
        Assert.Equal("normalized", scope.ServiceProvider.GetRequiredService<Probe>().ConstructorTenant);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<Probe>().Calls);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task InMemoryEngine_BindsTerminalWithoutFilters_AndRestoresAfterException()
    {
        using var caller = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        using var provider = services.BuildServiceProvider();
        var context = (InvocationContext)CreateContext().WithTenantId("operation");
        var expected = new InvalidOperationException("failed");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(context, new object(), async (current, _, _) =>
            {
                await Task.Yield();
                Assert.Same(current.Context, Context.Current);
                throw expected;
            }, default));
        Assert.Same(expected, actual);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task InMemoryEngine_CanceledInvocation_DoesNotResolveFiltersOrRunTerminal()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline().AddInvocationFilter<MustNotRun>();
        using var provider = services.BuildServiceProvider();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var terminalCalled = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(CreateContext(), new object(), (_, _, _) =>
            {
                terminalCalled = true;
                return ValueTask.CompletedTask;
            }, source.Token));
        Assert.False(terminalCalled);
    }

    private static InvocationContext CreateContext() => new()
    {
        HandlerType = typeof(InvocationPipelineTests),
        HandlerMethodInfo = typeof(InvocationPipelineTests).GetMethod(nameof(FilterRegistration_AloneDoesNotSelectOrRunAnEngine))!,
        Operation = new InvocationTestDescriptor(typeof(object), nameof(ToString), typeof(object), typeof(Empty))
    };

    public sealed class HostPipeline : IInvocationPipeline
    {
        public ValueTask ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken ct)
            => terminal((InvocationContext)context.WithTenantId("host"), input, ct);
    }

    public sealed class MustNotRun : IInvocationFilter
    {
        public ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
            => throw new InvalidOperationException("The host did not choose to run this filter");
    }

    public sealed class Normalize : IInvocationFilter
    {
        public ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
            => next((InvocationContext)context.WithTenantId("normalized"), input, ct);
    }

    public sealed class Probe : IInvocationFilter
    {
        public string ConstructorTenant { get; } = Context.Current.TenantId;
        public int Calls { get; private set; }
        public async ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
        {
            Calls++;
            await next(context, input, ct);
        }
    }
}
