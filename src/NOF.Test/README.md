# NOF.Test

Test hosting helpers for NOF application services, messaging and in-memory persistence.

```csharp
await using var host = await NOFTestAppBuilder.Create()
    .AddApplicationPartOf<OrderService>()
    .AddApplicationService<OrderService>()
    .AddInMemoryPersistence()
    .BuildTestHostAsync();

var result = await host.CallAsync<IOrderService.GetOperation.Result>(
    (services, context, ct) => OrderService.Get.InvokeAsync(
        services, new GetOrderRequest(orderId), context, ct),
    configure: scope => scope.SetTenant("tenant-a").SetUser("user-1", "Alice"));
```

`CallAsync<TResult>` accepts `(IServiceProvider, Context, CancellationToken)` and
returns the operation's task. Client generation, `AddLocalApplicationServiceClient`
and `GetRpcClient` have been removed. Add every assembly with generated initializers
explicitly, including test assemblies declaring mappings.

`NOFTestHost` exposes CreateScope, ExecuteAsync, CallAsync, SendAsync and PublishAsync.
Inside ExecuteAsync, configure the scope with SetTenant, SetUser, SetAnonymousUser,
SetTracing, SetContext, SetContextItem, SetContextItems or RemoveContextItem; resolve
dependencies with GetRequiredService. Scope CallAsync uses the configured Context.
Daemon services and Context.Current are bound for scoped ambient conveniences.
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
