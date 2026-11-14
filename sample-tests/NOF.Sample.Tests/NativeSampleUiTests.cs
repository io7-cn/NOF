using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NOF.Sample.UI.Pages;
using NOF.Sample.UI.Services;
using System.Net;
using Xunit;

namespace NOF.Sample.Tests;

public class NativeSampleUiTests
{
    [Theory]
    [InlineData(typeof(UI.Pages.Index), "配置中心")]
    [InlineData(typeof(StreamingDemo), "开始流式调用")]
    [InlineData(typeof(OidcDemo), "Authorize URL Builder")]
    [InlineData(typeof(OAuthChainDemo), "生成 Client")]
    public async Task DemoPagesRenderWithNativeControls(Type component, string expectedText)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<SampleMessageService>();
        services.AddSingleton<NavigationManager, SampleNavigationManager>();
        services.AddScoped(_ => new HttpClient(new RootNodesHandler()) { BaseAddress = new Uri("http://localhost/") });
        services.AddScoped<ISampleApi, HttpSampleApi>();
        services.AddScoped<IOAuthDemoApi, HttpOAuthDemoApi>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync(component, ParameterView.Empty)).ToHtmlString());

        Assert.Contains(expectedText, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("ant-", html);
        Assert.Contains("<button", html);
    }

    public sealed class SampleNavigationManager : NavigationManager
    {
        public SampleNavigationManager() => Initialize("http://localhost/", "http://localhost/");

        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    public sealed class RootNodesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/config-nodes", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
