using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public sealed class MessageDeliveryTests
{
    [Theory]
    [InlineData(true, "explicit")]
    [InlineData(false, "ambient")]
    public async Task Delivery_ShouldPrepareTenantAndStableMessageIdentity(bool hasTenant, string expected)
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("ambient"));
        var delivery = new MessageDelivery(Microsoft.Extensions.Options.Options.Create(new DeploymentOptions()));
        Dictionary<string, string?>? captured = null;
        await delivery.ExecuteAsync(new object(), hasTenant ? Context.Empty.WithTenantId("explicit") : Context.Empty,
            (headers, _) => { captured = headers; return ValueTask.CompletedTask; }, default);
        Assert.Equal(expected, captured![NOFAbstractionConstants.Transport.Headers.TenantId]);
        Assert.True(Guid.TryParse(captured[NOFAbstractionConstants.Transport.Headers.MessageId], out _));
        Assert.DoesNotContain(captured.Keys, key => key.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public async Task Delivery_BindsProducerTrace_AndPropagatesDispatchFailureUnchanged()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NOFHostingConstants.Outbound.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("caller").Start();
        var delivery = new MessageDelivery(Microsoft.Extensions.Options.Options.Create(new DeploymentOptions()));
        var expected = new InvalidOperationException("dispatch failed");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await delivery.ExecuteAsync(new object(), Context.Empty, (headers, _) =>
            {
                Assert.Equal(ActivityKind.Producer, Activity.Current!.Kind);
                Assert.Equal(parent.Id, Activity.Current.ParentId);
                Assert.Equal(Activity.Current.ToTraceParent(), headers[NOFAbstractionConstants.Transport.Headers.TraceParent]);
                throw expected;
            }, default));
        Assert.Same(expected, actual);
        Assert.Same(parent, Activity.Current);
    }

    [Fact]
    public async Task Delivery_Cancellation_DoesNotDispatch()
    {
        var delivery = new MessageDelivery(Microsoft.Extensions.Options.Options.Create(new DeploymentOptions()));
        using var source = new CancellationTokenSource();
        source.Cancel();
        var called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await delivery.ExecuteAsync(new object(), Context.Empty, (_, _) =>
            {
                called = true;
                return ValueTask.CompletedTask;
            }, source.Token));
        Assert.False(called);
    }
}
