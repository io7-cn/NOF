# NOF.Contract

[简体中文](../../zh-CN/packages/contract.md)

Transport-independent service declarations and data models.

Declare synchronous service methods with one reference-type input, a business success type and optional FailureCase/permission attributes. Void means one-way with no reply or FailureCase; Empty explicitly retains a reply. Interfaces do not need partial. Application generates Result, failures and descriptors. Contract contains no generated clients, HTTP routing, generic Result, execution contexts or executor interfaces. Optional preserves missing versus present-null values.

## Installation

```sh
dotnet add package NOF.Contract
```

## Usage and boundaries

- [contract-boundaries](../design/contract-boundaries.md)
- [expected-failures](../design/expected-failures.md)
- [one-way-operations](../design/one-way-operations.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
