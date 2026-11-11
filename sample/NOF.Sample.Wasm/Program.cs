using NOF.Hosting.BlazorWebAssembly;
using NOF.Sample;
using NOF.Sample.UI.Services;

var builder = NOFWebAssemblyHostBuilder.Create(args);

builder.Services.AddAntDesign();
builder.Services.AddScoped<ISampleApi, HttpSampleApi>();
builder.Services.AddScoped<IOAuthDemoApi, HttpOAuthDemoApi>();
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.WebAssemblyHostBuilder.HostEnvironment.BaseAddress)
});

var app = await builder.BuildAsync();
await app.RunAsync();
