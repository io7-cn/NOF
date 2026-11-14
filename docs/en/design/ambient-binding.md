# Explicit ambient binding

[简体中文](../../zh-CN/design/ambient-binding.md) · [Native hosting](native-hosting.md)

Keep IdGenerator/Mapper/EventPublisher convenience APIs and their explicit injected counterparts. Ordinary CreateScope/CreateAsyncScope is ordinary dependency injection: it neither activates NOF nor requires a NOF analyzer protocol. Resolving a service alone does not bind it globally.

HTTP UseNOF, operation execution, reliable message scopes and the NOF test scope automatically bind and restore their own semantic Context and available convenience services. Binding follows the current async flow, supports nesting/await and restores the caller on success or exception. Different hosts/tasks do not share bindings. Services absent from an inner host shadow the outer ambient value; unavailable Mapper/EventPublisher conveniences throw InvalidOperationException; IdGenerator.Current returns null.

Standalone execution uses one synchronous binding scope around its work:

```csharp
await using var scope = services.CreateAsyncScope();
using var binding = scope.ServiceProvider.BindNOF(context);

var id = ConfigNodeId.New();
var rows = query.ProjectTo<ConfigNodeDto>();
domainEvent.PublishAsEvent();
```

BindNOF does not create/dispose the DI scope, start workers or publish events. Dispose the binding before disposing its service scope. Services resolve lazily on first convenience access under the actual current Context; unused services are not constructed before invocation policies. Factory exceptions propagate, and disposing the binding restores the caller. Context remains immutable; invocation stages may use their own normalized views and HTTP never returns business Context to response mapping.

The implementation composes existing PushCurrent scopes; no daemon marker, activation registry or eager scope-creation protocol remains. A package-only consumer may still use its specific IdGenerator.PushCurrent/IMapper/EventPublisher helper without adding another layer. PushCurrent(null) explicitly shadows a previous service until disposal. BindNOF belongs to Application and can bind whichever services are registered.

PublishAsEvent retains its synchronous-wait publishing timing and exception semantics. This change does not introduce event recording/deferred dispatch or alter the unit of work. Prefer explicit await publisher.PublishAsync where asynchronous execution is already available.

Breaking migration: remove ResolveDaemonServices, IDaemonService implementations and ambient daemon registrations. NOF040 and its code fix are deleted. Replace standalone activation with using var binding = services.BindNOF(context); managed entries need no extra call. Custom scoped resources use ordinary DI lifetime/disposal rather than the removed marker. Upgrade host/test helpers together.

IdGenerator.Current returns IIdGenerator? and yields null when unbound, including an inner null binding. Generated value-object New() decides to throw when it needs a provider; explicit callers may choose another policy. Snowflake is an Infrastructure choice and Domain needs no registration.
