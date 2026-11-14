# NOF.Test

Test hosting helpers for NOF application services, messaging and in-memory persistence.

```csharp
var builder = Host.CreateApplicationBuilder().AddNOFTest();
builder.AddApplicationPartOf<OrderService>();
builder.AddApplicationService<OrderService>();
await using var host = builder.BuildTestHost();
// await host.Host.StartAsync(); // when the test needs migration, bootstrap or workers

var result = await host.CallAsync<object?>(
    (services, context, ct) => OrderService.Get.InvokeAsync(
        services, new GetOrderRequest(orderId), context, ct),
    configure: scope => scope.SetTenant("tenant-a").SetUser("user-1", "Alice"));
```

`CallAsync<TResult>` accepts `(IServiceProvider, Context, CancellationToken)` and
returns the operation's task. Client generation, `AddLocalApplicationServiceClient`
and `GetRpcClient` have been removed. Add every assembly with generated initializers
explicitly, including test assemblies declaring mappings.

`NOFTestHost` exposes CreateScope, ExecuteAsync and CallAsync; NOFTestScope.EnqueueAsync stages explicitly configured Outbox intents.
Inside ExecuteAsync, configure the scope with SetTenant, SetUser, SetAnonymousUser,
SetTracing, SetContext, SetContextItem, SetContextItems or RemoveContextItem; resolve
dependencies with GetRequiredService. Scope CallAsync uses the configured Context.
Test scopes bind and restore Context.Current and ambient conveniences through BindNOF.
Server invocation still creates its own policy-controlled inbound scope; use explicit
context and dependency APIs when tests need to observe those boundaries.

See [sample harness tests](../../sample-tests/NOF.Sample.Tests/NOFSampleHarnessTests.cs)
and the [migration guide](../../docs/en/design/application-requests.md).

## Installation

```shell
dotnet add package NOF.Test
```

## License

Apache-2.0

BuildTestHost only builds the native host. Startup and its cancellation/failures follow the native lifecycle. See [native hosting](../../docs/en/design/native-hosting.md).
