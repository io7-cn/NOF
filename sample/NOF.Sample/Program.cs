using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample;
using NOF.Sample.Application;
using NOF.Sample.Services;
using NOF.Sample.UI.Services;

var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.AddApplicationPart(typeof(NOF.Sample.Endpoints.ConfigNodeEndpoints).Assembly);
var sampleOrigin = ResolveSampleOrigin(builder.Configuration);
var sampleIssuer = $"{sampleOrigin}/oauth2";

builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
builder.AddApplicationService<MessageDemoService>();
builder.Services.AddMessageBindings(bindings =>
{
    bindings.Queue<SendEmail>("sample.mail.v1").To(IMessageDemoServiceOperations.SendMail);
    bindings.Subscription<OrderCreated>("sample.orders.created.v1", "sample-order-audit")
        .To(IMessageDemoServiceOperations.RecordOrder);
});
builder.AddApplicationService<OAuthChainDemoService>();
builder.AddApplicationService<DemoDownstreamService>();

builder.Services.AddRedisCache(builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException("Connection string 'redis' is required."));
builder.Services.Configure<CacheServiceOptions>(options => options.KeyPrefix = "nof-sample:{tenantId}:");
builder.Services.AddProblemDetails();

builder.AddOidcServer(o =>
{
    o.Issuer = sampleIssuer;
    o.AccessTokenAudience = "nof-sample";
    o.SigningKeyEncryptionKey = builder.Configuration["NOF:OidcServer:SigningKeyEncryptionKey"]
        ?? throw new InvalidOperationException("Configuration value 'NOF:OidcServer:SigningKeyEncryptionKey' not found.");
})
.AddPublicClient(
    "nof-sample-ui",
    ["openid", "profile", "email", "sample.read", "sample.write"],
    displayName: "NOF Sample UI",
    redirectUris: [$"{sampleOrigin}/oauth/callback", "https://client.local/oauth/callback"]);

builder.Services.AddAuthenticationResourceServer(o =>
{
    o.ExpectedIssuer = sampleIssuer;
    o.Audience = "nof-sample";
    o.RequireHttpsMetadata = false;
    o.AuthorizationServerIssuer = sampleIssuer;
});

builder.Services.AddRabbitMQ(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq");
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
builder.Services.AddScoped<IOAuthAuthorizeEndpoint, SampleOAuthAuthorizeEndpoint>();
builder.Services.AddScoped<IOAuthSubjectService, SampleOAuthSubjectService>();
builder.Services.AddHttpClient<OAuthChainDemoBackend>(client => client.BaseAddress = new Uri(sampleOrigin));
builder.Services.AddTransient<OAuthChainDemoAccessTokenHandler>();
builder.Services.AddHttpClient<IDemoDownstreamApi, DownstreamApi>(client => client.BaseAddress = new Uri(sampleOrigin))
    .AddHttpMessageHandler<OAuthChainDemoAccessTokenHandler>();

builder.Services.AddAntDesign();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();
app.UseNOF();
app.MapNOFEndpoints();

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
