# NOF.Hosting

Thin integration with native Microsoft.Extensions.Hosting abstractions: application-part
registration, DeploymentOptions, scoped lazy service resolution. Hosting references only NOF.Abstraction among NOF runtime packages; authentication stays with native host libraries.

There are no NOF host implementations, build wrappers or initialization step pipelines.
Use native builders, Build and platform lifecycle. Register application parts explicitly;
builder and IServiceCollection entry points share per-container deduplication.

See [native hosting and migration](../../docs/en/design/native-hosting.md), including its Chinese edition.
