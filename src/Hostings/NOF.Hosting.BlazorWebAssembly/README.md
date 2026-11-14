# NOF.Hosting.BlazorWebAssembly

Client registration extensions for the native WebAssemblyHostBuilder.

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args).AddNOF();
await builder.Build().RunAsync();
```

Registers NOF UI services only. There is no second IHost or server background-service loop.
See [native hosting](../../../docs/en/design/native-hosting.md).
