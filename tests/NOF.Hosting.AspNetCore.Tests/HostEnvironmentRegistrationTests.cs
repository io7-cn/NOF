using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Infrastructure;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class HostEnvironmentRegistrationTests
{
    [Fact]
    public async Task Build_ShouldResolveBuilderEnvironmentFromServices()
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseNOF();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive");

        var resolvedEnvironment = app.Services.GetRequiredService<IHostEnvironment>();

        Assert.Same(builder.Environment, app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
        Assert.Equal(builder.Environment.ApplicationName, resolvedEnvironment.ApplicationName);
    }

    [Fact]
    public async Task Build_InDevelopmentWithoutExplicitPersistence_ShouldSucceed()
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseNOF();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive");

        Assert.Null(app.Services.GetService<Application.IUnitOfWork>());
    }
}
