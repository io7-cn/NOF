# NOF.Domain

[简体中文](../../zh-CN/packages/domain.md)

Value objects and domain ID contracts.

Domain owns business invariants, IValueObject<T>, IIdGenerator and the nullable IdGenerator.Current convenience accessor. Implement IsValid(value, out errorMessage) and optional Normalize; From/TryFrom are generated. ReadOnlyCollection generates cached live read-only collection views. Domain registers no services and depends on no Contract or transport. Technical misuse uses BCL exceptions; business rejection is mapped to Application failure cases.

## Installation

```sh
dotnet add package NOF.Domain
```

## Usage and boundaries

- [value-objects](../design/value-objects.md)
- [value-object](../design/value-object.md)
- [unified-operations](../design/unified-operations.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
