using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using NOF.Abstraction;
using System.Text.Json;

namespace NOF.Hosting.AspNetCore;

/// <summary>Integrates NOF with native ASP.NET Core registration and application composition.</summary>
public static class WebApplicationExtensions
{
    private static readonly object RegistrationKey = new();
    /// <summary>Registers execution and HTTP adapter prerequisites, including JSON, ProblemDetails and OpenAPI, without building or starting the application.</summary>
    public static WebApplicationBuilder AddNOF(this WebApplicationBuilder builder)
    {
        var properties = ((IHostApplicationBuilder)builder).Properties;
        if (properties.ContainsKey(RegistrationKey))
        {
            return builder;
        }
        properties[RegistrationKey] = true;
        ((IHostApplicationBuilder)builder).AddNOF();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, NOFJsonSerializerContext.Default));
        builder.Services.AddHealthChecks().AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<HttpRequestInboundAdapter>();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, NOFHttpJsonContext.Default));
        builder.Services.AddOpenApi(options => options.AddOperationTransformer<HttpBindingOpenApiTransformer>());
        return builder;
    }

    /// <summary>Installs scoped ambient services for the native HTTP request pipeline.</summary>
    public static WebApplication UseNOF(this WebApplication app)
    {
        if (!((IApplicationBuilder)app).Properties.TryAdd("NOF.HttpMiddleware", true))
        {
            return app;
        }
        app.UseMiddleware<NOFAmbientBindingMiddleware>();
        return app;
    }

}
