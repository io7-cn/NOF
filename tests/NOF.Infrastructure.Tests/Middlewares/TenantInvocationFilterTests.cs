using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public class TenantInvocationFilterTests
{
    [Fact]
    public async Task InvokeAsync_WithoutIncomingTenantHeader_ShouldUseHostTenant()
    {
        var middleware = new TenantInvocationFilter();
        var message = new object();
        var inboundContext = CreateContext(message.GetType());
        var tenantDuringNext = string.Empty;

        await middleware.InvokeAsync(inboundContext, message, CaptureNextContext, default);
        Assert.Equal(NOFAbstractionConstants.Tenant.HostId, tenantDuringNext);

        ValueTask<object?> CaptureNextContext(InvocationContext context, object forwardedMessage, CancellationToken cancellationToken)
        {
            _ = forwardedMessage;
            _ = cancellationToken;
            tenantDuringNext = context.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithIncomingTenantHeader_ShouldUseIncomingTenantHeader()
    {
        var middleware = new TenantInvocationFilter();
        var message = new object();
        var inboundContext = (InvocationContext)CreateContext(message.GetType())
            .WithItem(NOFAbstractionConstants.Transport.Headers.TenantId, "tenanta");
        var tenantDuringNext = string.Empty;

        await middleware.InvokeAsync(inboundContext, message, CaptureNextContext, default);
        Assert.Equal("tenanta", tenantDuringNext);

        ValueTask<object?> CaptureNextContext(InvocationContext context, object forwardedMessage, CancellationToken cancellationToken)
        {
            _ = forwardedMessage;
            _ = cancellationToken;
            tenantDuringNext = context.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithLowercaseIncomingTenantHeader_ShouldUseIncomingTenantHeader()
    {
        var middleware = new TenantInvocationFilter();
        var message = new object();
        var inboundContext = (InvocationContext)CreateContext(message.GetType())
            .WithItem("x-tenant-id", "tenanta");
        var tenantDuringNext = string.Empty;

        await middleware.InvokeAsync(inboundContext, message, CaptureNextContext, default);
        Assert.Equal("tenanta", tenantDuringNext);

        ValueTask<object?> CaptureNextContext(InvocationContext context, object forwardedMessage, CancellationToken cancellationToken)
        {
            _ = forwardedMessage;
            _ = cancellationToken;
            tenantDuringNext = context.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }
    }

    private static InvocationContext CreateContext(Type messageType)
    {
        return new InvocationContext
        {
            HandlerMethodInfo = typeof(object).GetMethod(nameof(ToString))!,
            HandlerType = typeof(object),
            Operation = new InvocationTestDescriptor(typeof(object), (typeof(object).GetMethod(nameof(ToString))!).Name, messageType, typeof(Empty)),
            Origin = InvocationOrigin.Message
        };
    }
}
