using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public sealed class TracingInvocationFilterTests
{
    [Fact]
    public async Task InvokeAsync_ServiceCancellation_ShouldPropagateWithoutBecomingARejection()
    {
        var context = new InvocationContext
        {
            HandlerType = typeof(ServiceOperationPipelineTests.ProbeOperation),
            HandlerMethodInfo = typeof(ServiceOperationPipelineTests.ProbeOperation).GetMethod("HandleAsync",
                [typeof(ServiceOperationPipelineTests.Query), typeof(InvocationContext), typeof(CancellationToken)])!,
            Operation = new InvocationTestDescriptor(typeof(ServiceOperationPipelineTests.IProbeService), (typeof(ServiceOperationPipelineTests.IProbeService).GetMethod("Read")!).Name, typeof(ServiceOperationPipelineTests.Query), typeof(ServiceOperationPipelineTests.Reply)),
            Origin = InvocationOrigin.Http
        };
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await CreateMiddleware().InvokeAsync(
            context, new ServiceOperationPipelineTests.Query(), (_, _, token) => ValueTask.FromCanceled<object?>(token), source.Token));
    }

    [Fact]
    public async Task InvokeAsync_CommandException_ShouldPropagate()
    {
        var middleware = CreateMiddleware();
        var exception = new InvalidOperationException("Command failed.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.InvokeAsync(
                CreateCommandContext(),
                new TestCommand(),
                (_, _, _) => throw exception,
                default));

        Assert.Same(exception, actual);
    }

    [Fact]
    public async Task InvokeAsync_NotificationException_ShouldPropagate()
    {
        var middleware = CreateMiddleware();
        var exception = new InvalidOperationException("Notification failed.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.InvokeAsync(
                CreateNotificationContext(),
                new TestNotification(),
                (_, _, _) => throw exception,
                default));

        Assert.Same(exception, actual);
    }

    private static TracingInvocationFilter CreateMiddleware()
        => new(
            Microsoft.Extensions.Options.Options.Create(new NOF.Hosting.DeploymentOptions()),
            NullLogger<TracingInvocationFilter>.Instance);

    private static InvocationContext CreateCommandContext()
        => new()
        {
            HandlerMethodInfo = typeof(TestHandler).GetMethod(nameof(TestHandler.HandleCommand))!,
            HandlerType = typeof(TestHandler),
            Operation = new InvocationTestDescriptor(typeof(TestHandler), nameof(TestHandler.HandleCommand), typeof(TestCommand), typeof(Empty)),
            Origin = InvocationOrigin.Message
        };

    private static InvocationContext CreateNotificationContext()
        => new()
        {
            HandlerMethodInfo = typeof(TestHandler).GetMethod(nameof(TestHandler.HandleNotification))!,
            HandlerType = typeof(TestHandler),
            Operation = new InvocationTestDescriptor(typeof(TestHandler), nameof(TestHandler.HandleNotification), typeof(TestNotification), typeof(Empty)),
            Origin = InvocationOrigin.Message
        };

    private sealed class TestCommand;

    private sealed class TestNotification;

    private sealed class TestHandler
    {
        public void HandleCommand(TestCommand command) => _ = command;

        public void HandleNotification(TestNotification notification) => _ = notification;
    }
}
