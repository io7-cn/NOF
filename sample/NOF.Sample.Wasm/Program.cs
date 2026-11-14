using NOF.Hosting.BlazorWebAssembly;
using NOF.Sample;
using NOF.Sample.UI.Services;

var builder = Microsoft.AspNetCore.Components.WebAssembly.Hosting.WebAssemblyHostBuilder.CreateDefault(args).AddNOF();

builder.Services.AddScoped<SampleMessageService>();
builder.Services.AddScoped<ISampleApi, HttpSampleApi>();
builder.Services.AddScoped<IOAuthDemoApi, HttpOAuthDemoApi>();
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

var app = builder.Build();
await app.RunAsync();
