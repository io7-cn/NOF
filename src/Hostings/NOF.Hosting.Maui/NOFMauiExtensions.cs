using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace NOF.Hosting.Maui;

/// <summary>Registers client-only NOF capabilities on the native MAUI builder.</summary>
public static class NOFMauiExtensions
{
    /// <summary>Adds UI services without adapting MauiApp to a generic host.</summary>
    public static MauiAppBuilder AddNOF(this MauiAppBuilder builder)
    {
        builder.Services.AddNOFUI();
        return builder;
    }
}
