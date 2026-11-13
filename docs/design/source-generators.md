# Source Generator Isolation

Every NOF source generator treats generator output as unavailable input. A generator may inspect only user-authored declarations and stable types from referenced runtime assemblies.

Generated types may still be referenced in emitted source, but their names and signatures must be derived independently from the same user-authored declaration. Generators must never query another generator's emitted symbol, members, attributes, interfaces, or base types through Roslyn's semantic model.

The current generator inputs are:

| Package | Generator | Stable input |
| --- | --- | --- |
| Abstraction | `AutoInjectGenerator` | User class and `AutoInjectAttribute` |
| Abstraction | `EventHandlerRegistrationGenerator` | User class and `InMemoryEventHandler<T>` |
| Contract | `FailureCaseGenerator` | User partial service interface, success signatures and FailureCase attributes |
| Domain | `ValueObjectGenerator` | User struct, `IValueObject<T>`, and `ValueObjectLengthAttribute` |
| Application | `HandlerRegistrationGenerator` | User class and stable command/notification handler bases |
| Application | `MappableGenerator` | User mapping declaration, attributes, and mapped source/destination types |
| Application | `ApplicationServiceGenerator` | User `ApplicationService<TService>` and RPC service contract |
| Application | `ApplicationServiceAutoInjectGenerator` | User `Server.Operation` base-type syntax, `ApplicationService<TService>`, and RPC service contract |

`ApplicationServiceGenerator` emits server operation bases, mappings and typed static
`Service.Operation.InvokeAsync` entry points and transport-neutral typed descriptors. HTTP route generators are deleted. Contract generates operation bases containing canonical Result unions and failure cases. Application operation bases inherit them without generating factories, deriving their names independently from authored attributes. Contract generates declared expected-outcome unions and validates transport-neutral declarations; native C# checks union switch-expression exhaustiveness. Client-interface, HTTP-client and
Local-client generators and their shared naming conventions have been removed.

The regression suite runs every generator together in one compilation in both normal and reversed order. This represents a monolithic application that references every NOF package and prevents generator ordering from becoming an implicit contract.

## Code Fixes

NOF runtime packages ship a shared `NOF.CodeFixes` analyzer assembly beside their layer-specific source generator. A code fix is offered only when the diagnostic has a deterministic local transformation, including required type modifiers, invalid or duplicate attribute removal, value-object primitive ordering casts, daemon-service resolution, supported RPC request/signature corrections, direct `DbContext` base replacement, and value-object length migration.

All provided code fixes support Fix All at document, project, and solution scope. HTTP transport diagnostics and their code fixes are deleted along with the legacy adapters.

Diagnostics that require a domain or API design choice deliberately remain analyzer-only. Examples include renaming overloaded operations, inventing unique failure names/codes, or rewriting `Find` calls without a known key predicate.

The outbound middleware dispatcher-injection analyzer is removed along with the outbound middleware extension surface. Invocation filters are ordinary Application contracts, registered without source generation.

Native host Build is supported directly; NOF401, its code fix, and the now-empty NOF.Hosting.SourceGenerator project/package analyzer are removed. See [native hosting](../en/design/native-hosting.md).
