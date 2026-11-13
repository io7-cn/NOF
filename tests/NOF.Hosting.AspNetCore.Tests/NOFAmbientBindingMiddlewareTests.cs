using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class NOFAmbientBindingMiddlewareTests
{
    [Fact]
    public async Task Requests_BindScopedServices_AndRestoreTheCallerAfterFailure()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNOFApplication();
        await using var app = builder.Build();
        app.UseMiddleware<NOFAmbientBindingMiddleware>();
        app.MapGet("/ping", (HttpContext http) =>
        {
            Assert.Same(http.RequestServices.GetRequiredService<IMapper>(), Mapper.Current);
            return TypedResults.Ok();
        });
        app.MapGet("/broken", (HttpContext http) =>
        {
            Assert.Same(http.RequestServices.GetRequiredService<IMapper>(), Mapper.Current);
            throw new InvalidOperationException("expected");
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/ping");
        response.EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/broken"));
        Assert.Throws<InvalidOperationException>(() => Mapper.Current);
    }
}
