# Handler registration

[简体中文](../../zh-CN/design/handler-registration.md)

Source generators emit assembly-level `AssemblyInitializeAttribute` initializers. `AddApplicationPart(assembly)` runs them against the current dependency-injection container. There is no process-wide type registry or registration-step pipeline.

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
```

Application registers scoped operation implementations from authored service bases and Contract metadata, without reading another generator's output. Operations are identified by service plus method, so request/response models may be shared. Concrete standalone `OperationHandler` classes need no Contract method and are discovered through the same application part. Outbox and local-event handlers are also discovered without constructing instances during registration.

Registries such as `ApplicationServiceRegistry`, `EventHandlerRegistry` and `MappingRegistry` belong to one container and freeze on first read. Add owning assemblies before mapping endpoints or executing operations. Repeated application-part registration is deduplicated per container. Add test assemblies explicitly when they contain mappings or handlers. Nothing discovers the caller assembly implicitly, generates clients or enables Outbox workers merely by discovering a handler.

See [application operations](application-requests.md), [standalone operations](unified-operations.md) and [source generator isolation](source-generators.md).
