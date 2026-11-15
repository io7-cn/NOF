using Microsoft.AspNetCore.Builder;
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
builder.AddApplicationService<OAuthChainDemoService>();
builder.AddApplicationService<DemoDownstreamService>();

builder.Services.AddOutbox();
builder.Services.AddInbox();
builder.Services.Configure<TransactionalMessageOptions>(options =>
{
    options.Outbox.PollingInterval = TimeSpan.FromSeconds(1);
    options.Inbox.RunWorkers = false;
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
builder.Logging.AddOpenTelemetry(options => { options.IncludeFormattedMessage = true; options.IncludeScopes = true; });
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

var sampleOrigin = ResolveSampleOrigin(builder.Configuration);
var sampleIssuer = $"{sampleOrigin}/oauth2";

builder.Services.AddScoped<IMessageClient, RabbitMQMessageClient>();
builder.Services.AddSampleIdentity<ConfigurationDbContext>(new Uri(sampleIssuer + "/"), builder.Environment.IsDevelopment());

builder.Services.Replace(ServiceDescriptor.Scoped<ISampleApi, ServerSampleApi>());
builder.Services.Replace(ServiceDescriptor.Scoped<IOAuthDemoApi, ServerOAuthDemoApi>());
builder.Services.AddScoped<SampleMessageService>();

builder.Services.AddHttpClient<OAuthChainDemoBackend>(client => client.BaseAddress = new Uri(sampleOrigin))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddTransient<OAuthChainDemoAccessTokenHandler>();
builder.Services.AddHttpClient<IDemoDownstreamApi, DownstreamApi>(client => client.BaseAddress = new Uri(sampleOrigin))
    .AddHttpMessageHandler<OAuthChainDemoAccessTokenHandler>();

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
if (app.Environment.IsDevelopment())
{
    app.MapOAuthDemoEndpoints();
}
app.MapSampleIdentity(app.Environment.IsDevelopment());
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

static string ResolveSampleOrigin(IConfiguration configuration)
{
    var urls = configuration["ASPNETCORE_URLS"];
    if (!string.IsNullOrWhiteSpace(urls))
    {
        var candidate = urls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Authority}";
        }
    }

    return "http://localhost";
}
