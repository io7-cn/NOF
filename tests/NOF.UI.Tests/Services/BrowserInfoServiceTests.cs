using Xunit;

namespace NOF.UI.Tests.Services;

public sealed class BrowserInfoServiceTests
{
    [Fact]
    public async Task RefreshAsync_ShouldPropagateJavaScriptFailure()
    {
        var failure = new Microsoft.JSInterop.JSException("Browser unavailable");
        var jsRuntime = new TestJSRuntime
        {
            FailureIdentifier = "NOF.UI.browserInfo.get",
            Failure = failure
        };
        await using var service = new BrowserInfoService(jsRuntime);

        var exception = await Assert.ThrowsAsync<Microsoft.JSInterop.JSException>(() => service.RefreshAsync().AsTask());

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task DisposeAsync_ShouldTolerateDisconnectAndStopOnlyOnce()
    {
        var jsRuntime = new TestJSRuntime
        {
            FailureIdentifier = "NOF.UI.browserInfo.unsubscribe",
            Failure = new Microsoft.JSInterop.JSDisconnectedException("Browser disconnected")
        };
        var service = new BrowserInfoService(jsRuntime);
        await service.GetAsync();

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Single(jsRuntime.Invocations, identifier => identifier == "NOF.UI.browserInfo.unsubscribe");
    }

    [Fact]
    public async Task NotifyChanged_ShouldUpdateCacheAndRaiseChangedEvent()
    {
        var jsRuntime = new TestJSRuntime();
        var service = new BrowserInfoService(jsRuntime);
        BrowserInfoChangedEventArgs? eventArgs = null;
        service.Changed += (_, args) => eventArgs = args;

        var browserInfo = new BrowserInfo
        {
            UserAgent = "test-agent",
            Viewport = new BrowserViewportInfo { Width = 1920, Height = 1080 }
        };

        await service.NotifyChanged("Resize", browserInfo);
        var currentInfo = await service.GetAsync();

        Assert.NotNull(eventArgs);
        Assert.Equal(BrowserInfoChangeKind.Resize, eventArgs!.ChangeKind);
        Assert.Same(browserInfo, eventArgs.BrowserInfo);
        Assert.Same(browserInfo, currentInfo);
    }

    [Fact]
    public async Task Constructor_ShouldSubscribeAutomatically()
    {
        var jsRuntime = new TestJSRuntime();
        _ = new BrowserInfoService(jsRuntime);

        await Task.Yield();

        Assert.Contains("NOF.UI.browserInfo.subscribe", jsRuntime.Invocations);
    }

    private sealed class TestJSRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public List<string> Invocations { get; } = [];

        public string? FailureIdentifier { get; init; }

        public Exception? Failure { get; init; }

        // This fake forwards to its own implementation; no JavaScript boundary is crossed.
#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
#pragma warning restore BL0016

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations.Add(identifier);

            if (identifier == FailureIdentifier && Failure is { } failure)
            {
                return ValueTask.FromException<TValue>(failure);
            }

            if (typeof(TValue).FullName == "Microsoft.JSInterop.Infrastructure.IJSVoidResult")
            {
                return ValueTask.FromResult(default(TValue)!);
            }

            return ValueTask.FromResult((TValue)(object)new BrowserInfo());
        }
    }
}
