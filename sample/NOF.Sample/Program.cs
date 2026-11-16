using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample;
using NOF.Sample.Application;
using NOF.Sample.Endpoints;
using NOF.Sample.Identity;
using NOF.Sample.Services;
using NOF.Sample.UI.Services;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.Services.AddNOFUI();

builder.AddApplicationPart(typeof(ConfigNodeEndpoints).Assembly);
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
builder.AddApplicationService<MessageDispatchService>();
builder.AddApplicationService<PermissionDemoService>();

builder.Services.AddOutbox();
builder.Services.Configure<OutboxOptions>(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.RetryDelay = TimeSpan.FromSeconds(5);
    options.MaxRetryCount = 20;
});

builder.Services.AddIdGenerator()
    .AddSnowflakeIdGenerator();
builder.Services.AddObjectStorage()
    .AddFileSystemObjectStorage();
builder.Services.AddCacheService()
    .ConfigureCacheServiceOptions(options => options.KeyPrefix = "nof-sample:{tenantId}:")
    .AddRedisCache(
        builder.Configuration.GetConnectionString("redis")
            ?? throw new InvalidOperationException("Connection string 'redis' is required."));

builder.UseDbContext<ConfigurationDbContext>((creation, ef) => ef.UseNpgsql(
        DbConnectionStringTemplateResolver.ResolveTenantId(builder.Configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException("Connection string 'postgres' not found in configuration."), creation.TenantId)))
    .ConfigureNOFDbContextOptions(options =>
    {
        options.TenantMode = TenantMode.DatabasePerTenant;
        options.SoftDeleteEnabled = true;
    })
    .MigrateOnStart();

builder.Services.AddRabbitMQ(options =>
{
    options.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required."));
});

// Telemetry belongs to the native host: NOF supplies only ActivitySource and Meter instrumentation.
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(NOFInfrastructureConstants.InboundPipeline.ActivitySourceName, NOFInfrastructureConstants.OutboundPipeline.ActivitySourceName)
        .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(NOFInfrastructureConstants.InboundPipeline.MeterName)
        .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
}

builder.Services.AddSampleIdentity(builder.Configuration, builder.Environment);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton<DemoDeliveryControl>();
builder.Services.AddSingleton<DemoObservations>();
builder.Services.AddInvocationFilter<DemoInvocationFilter>();
builder.Services.AddHttpClient("notifications", client => client.BaseAddress = new Uri(
    builder.Configuration["Notifications:BaseUrl"] ?? "http://localhost:55893"));
builder.Services.Replace(ServiceDescriptor.Scoped<ISampleApi, ServerSampleApi>());
builder.Services.AddScoped<SampleMessageService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseNOF();
app.MapConfigNodeEndpoints();
app.MapMessageDispatchEndpoints();
app.MapPermissionDemoEndpoints();
app.MapSampleIdentity();
app.MapDemoCapabilities();
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

app.UseStatusCodePages();
app.MapOpenApi();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(NOF.Sample.UI.Components.Routes).Assembly,
        typeof(NOF.Sample.Wasm.WasmMarker).Assembly);

await app.RunAsync();
