using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace NOF.Hosting.BlazorWebAssembly;

/// <summary>Registers client-only NOF capabilities on the platform builder.</summary>
public static class NOFWebAssemblyExtensions
{
    /// <summary>Adds UI services without creating a second host or starting server background services.</summary>
    public static WebAssemblyHostBuilder AddNOF(this WebAssemblyHostBuilder builder)
    {
        builder.Services.AddNOFUI();
        return builder;
    }
}
