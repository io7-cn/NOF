# NOF.Hosting.Maui

Client registration extensions for native MauiAppBuilder.

```csharp
var builder = MauiApp.CreateBuilder().AddNOF();
builder.UseMauiApp<App>();
return builder.Build();
```

Registers NOF UI services only; the platform owns application lifetime. No IHost adapter,
synchronous async-build bridge or server background services are installed.
See [native hosting](../../../docs/en/design/native-hosting.md).
