using Microsoft.Extensions.Logging;
using NOF.Hosting.Maui;
using NOF.Sample;
using NOF.Sample.UI.Services;

namespace NOF.Sample.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().AddNOF();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddAntDesign();
        builder.Services.AddScoped<ISampleApi, HttpSampleApi>();
        builder.Services.AddScoped(_ => new HttpClient
        {
            BaseAddress = new Uri(builder.Configuration["SampleApiBaseAddress"] ?? "https://localhost:5001/")
        });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
