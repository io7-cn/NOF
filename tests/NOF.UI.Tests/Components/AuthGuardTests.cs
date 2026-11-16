using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.UI.Components;
using System.Collections.Concurrent;
using System.Security.Claims;
using Xunit;

namespace NOF.UI.Tests.Components;

public sealed class AuthGuardTests
{
    [Theory]
    [InlineData(false, "read", false)]
    [InlineData(true, "read", true)]
    [InlineData(true, "write", false)]
    public async Task Guard_UsesSharedChecksWithoutAnyUseCaseBinding(bool authenticated, string granted, bool expected)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        if (authenticated)
        {
            scope.ServiceProvider.GetRequiredService<IUserContext>().User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Permission, granted)], "test"));
        }
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<AuthGuard>(Parameters(new(true, [new(["read"])]))));
        Assert.Equal(expected ? "allowed" : "denied", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
    }

    [Fact]
    public async Task Guard_AnonymousPolicy_AllowsDisplayWithoutIdentity_AndAllAnyWorkTogether()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var anonymous = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<AuthGuard>(Parameters(AuthorizationPolicy.Anonymous)));
        Assert.Equal("allowed", await renderer.Dispatcher.InvokeAsync(anonymous.ToHtmlString));
        var user = scope.ServiceProvider.GetRequiredService<IUserContext>();
        user.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Permission, "read")], "test"));
        var policy = new AuthorizationPolicy(true, [new(["read"]), new(["read", "write"], PermissionMatch.Any)]);
        var guarded = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<AuthGuard>(Parameters(policy)));
        Assert.Equal("allowed", await renderer.Dispatcher.InvokeAsync(guarded.ToHtmlString));
        await renderer.Dispatcher.InvokeAsync(user.Logout);
        await WaitForAsync(renderer, guarded, "denied");
    }

    [Fact]
    public async Task Guard_StaleGrantCannotOverwriteNewerDenial_WhenCheckerIgnoresCancellation()
    {
        var checker = new DelayedChecker();
        var services = Services();
        services.AddScoped<IPermissionChecker>(_ => checker);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<IUserContext>();
        user.User.AddIdentity(new ClaimsIdentity("test"));
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<AuthGuard>(Parameters(new(true))));
        Assert.Equal("checking", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.True(checker.Calls.TryDequeue(out var first));
        await renderer.Dispatcher.InvokeAsync(user.Logout);
        Assert.True(checker.Calls.TryDequeue(out var second));
        second.SetResult(new PermissionCheckResult.AuthenticationRequired());
        await WaitForAsync(renderer, root, "denied");
        first.SetResult(new PermissionCheckResult.Granted());
        await root.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("denied", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
    }

    [Fact]
    public async Task RouteWrapper_RequiresAnExplicitUiPolicy()
    {
        using var provider = Services().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => renderer.Dispatcher.InvokeAsync(() =>
            renderer.RenderComponentAsync<PermissionRouteView>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PermissionRouteView.RouteData)] = new RouteData(typeof(ComponentBase), new Dictionary<string, object?>())
            }))));
    }

    private static IServiceCollection Services()
        => new ServiceCollection().AddLogging().AddNOFUI();

    private static ParameterView Parameters(AuthorizationPolicy policy) => ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(AuthGuard.Policy)] = policy,
        [nameof(AuthGuard.ChildContent)] = (RenderFragment)(builder => builder.AddContent(0, "allowed")),
        [nameof(AuthGuard.UnauthorizedContent)] = (RenderFragment)(builder => builder.AddContent(0, "denied")),
        [nameof(AuthGuard.AuthorizingContent)] = (RenderFragment)(builder => builder.AddContent(0, "checking"))
    });

    private static async Task WaitForAsync(HtmlRenderer renderer, HtmlRootComponent root, string expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await renderer.Dispatcher.InvokeAsync(root.ToHtmlString) != expected)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public sealed class DelayedChecker : IPermissionChecker
    {
        public ConcurrentQueue<TaskCompletionSource<PermissionCheckResult>> Calls { get; } = new();
        public ValueTask<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PermissionCheckResult> CheckAsync(AuthorizationPolicy policy, ClaimsPrincipal user, Context context, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<PermissionCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Enqueue(completion);
            return new(completion.Task);
        }
    }
}
