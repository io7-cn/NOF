using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
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

builder.AddApplicationPart(typeof(ConfigNodeEndpoints).Assembly);

builder.Services.AddIdGenerator()
    .AddSnowflakeIdGenerator();
builder.Services.AddObjectStorage()
    .AddFileSystemObjectStorage();

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

builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
builder.AddApplicationService<MessageDispatchService>();
builder.Services.AddScoped<IMessageClient, RabbitMQMessageClient>();
builder.Services.AddScoped<MessageDelivery>();
builder.Services.AddOutbox(outbox =>
{
    outbox.Add<SendEmail, MessageOutboxHandler>("sample.send-email", 1);
    outbox.Add<OrderCreated, MessageOutboxHandler>("sample.order-created", 1);
});
// Preserve the existing PostgreSQL migration model while moving consumers to a separate service.
builder.Services.AddInbox();
builder.Services.Configure<TransactionalMessageOptions>(options =>
{
    options.Outbox.PollingInterval = TimeSpan.FromSeconds(1);
    options.Inbox.RunWorkers = false;
});
builder.AddApplicationService<OAuthChainDemoService>();
builder.AddApplicationService<DemoDownstreamService>();

builder.Services.AddCacheService()
    .AddRedisCache(builder.Configuration.GetConnectionString("redis") ?? throw new InvalidOperationException("Connection string 'redis' is required."));
builder.Services.Configure<CacheServiceOptions>(options => options.KeyPrefix = "nof-sample:{tenantId}:");

builder.Services.AddSampleIdentity<ConfigurationDbContext>(new Uri(sampleIssuer + "/"), builder.Environment.IsDevelopment());

builder.Services.AddRabbitMQ(options =>
{
    options.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required."));
});

builder.UseDbContext<ConfigurationDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithSoftDelete(true)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found in configuration."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnStart();

builder.Services.ReplaceOrAddScoped<ISampleApi, ServerSampleApi>();
builder.Services.ReplaceOrAddScoped<IOAuthDemoApi, ServerOAuthDemoApi>();

builder.Services.AddHttpClient<OAuthChainDemoBackend>(client => client.BaseAddress = new Uri(sampleOrigin))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddTransient<OAuthChainDemoAccessTokenHandler>();
builder.Services.AddHttpClient<IDemoDownstreamApi, DownstreamApi>(client => client.BaseAddress = new Uri(sampleOrigin))
    .AddHttpMessageHandler<OAuthChainDemoAccessTokenHandler>();

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
