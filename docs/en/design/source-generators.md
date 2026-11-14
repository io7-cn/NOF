# Source generator isolation

[简体中文](../../zh-CN/design/source-generators.md) · [HTTP adaptation](http-adaptation.md)

Generators inspect user declarations and stable referenced metadata only. They may emit references to generated types, deriving their names independently from authored declarations, but cannot read another generator's outputs in the same compilation.

Abstraction generates registrations/local event registration; Domain generates value objects/collections; Application generates operation bases, closed Result/failure cases, static descriptors/visitor bridges and registration/mappings. Contract.SourceGenerator retains declaration analyzers and generates no Contract code. Infrastructure generates filter failure branches/catalogs. EF Core generates provider mapping support.

Hosting.AspNetCore.SourceGenerator emits only exact Request configuration extensions for MapGet/MapPost/MapPut/MapPatch/MapDelete/MapOperation<Operation>. It reads an existing referenced operation base or independently resolves the authored ApplicationService<TContract> and method, including inherited contracts. It never generates routes/clients, infers HTTP schemas from functions, or executes user delegates. Runtime OpenAPI describes final immutable endpoint bindings.

The combined regression suite runs generators in both orders against a monolithic application, including generic HTTP operation maps with a typed request lambda. Separate metadata-only Contract/native-union tests and reflection-disabled HTTP smoke cover assembly boundaries. Generic operation constraints reject unrelated types at compilation. NuGet embeds analyzers; ProjectReference consumers explicitly reference generators as analyzers.

NOF.CodeFixes ships deterministic local fixes alongside layer analyzers. Semantic design choices remain analyzer-only; native C# checks union exhaustiveness. No checked-result analyzer, old HTTP route/client generator, initialization generator or NOF401 remains.
