using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using NOF.Abstraction;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Text.Json;

namespace NOF.Hosting.AspNetCore;

/// <summary>Integrates NOF with native ASP.NET Core registration and application composition.</summary>
public static class WebApplicationExtensions
{
    private static readonly object RegistrationKey = new();
    /// <summary>Registers server defaults and HTTP adapters without building or starting the application.</summary>
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
        {
            var nof = JsonSerializerOptions.NOF;
            options.SerializerOptions.PropertyNameCaseInsensitive = nof.PropertyNameCaseInsensitive;
            options.SerializerOptions.DefaultIgnoreCondition = nof.DefaultIgnoreCondition;
            options.SerializerOptions.ReferenceHandler = nof.ReferenceHandler;
            options.SerializerOptions.PropertyNamingPolicy = nof.PropertyNamingPolicy;

            foreach (var converter in nof.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }

            options.SerializerOptions.TypeInfoResolver = nof.TypeInfoResolver;
        });
        builder.Services.AddHealthChecks().AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<HttpRequestInboundAdapter>();
        builder.Services.AddOptions<HttpRequestHeaderOptions>();
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddAspNetCoreInstrumentation());
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddAspNetCoreInstrumentation(options =>
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health")
                    && !context.Request.Path.StartsWithSegments("/alive")));
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(static (document, _, _) =>
            {
                const string scheme = NOFAbstractionConstants.Transport.Headers.Authorization;

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[scheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = scheme
                };

                document.Security ??= [];
                var requirement = new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(scheme, document, string.Empty)] = []
                };
                document.Security.Add(requirement);

                return Task.CompletedTask;
            });
        });
        return builder;
    }

    /// <summary>Installs scoped ambient services for the native HTTP request pipeline.</summary>
    public static WebApplication UseNOF(this WebApplication app)
    {
        if (!((IApplicationBuilder)app).Properties.TryAdd("NOF.HttpMiddleware", true))
        {
            return app;
        }
        app.UseMiddleware<DaemonServiceResolutionMiddleware>();
        return app;
    }

    /// <summary>Maps registered endpoint modules and health checks once, before the host starts.</summary>
    public static WebApplication MapNOFEndpoints(this WebApplication app)
    {
        if (!((IApplicationBuilder)app).Properties.TryAdd("NOF.HttpEndpoints", true))
        {
            return app;
        }
        foreach (var module in app.Services.GetServices<IHttpEndpointModule>())
        {
            module.Map(app);
        }
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live")
        });
        return app;
    }
}
