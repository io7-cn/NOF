# NOF.Hosting

[简体中文](../../zh-CN/packages/hosting.md)

Application parts and native host composition.

Use native builders and Build. AddApplicationPart applies generated initializers once per container; AddApplicationService registers service mappings. DeploymentOptions and scoped lazy services remain composition helpers. There is no second IHost, initialization-step pipeline or asynchronous Build wrapper. Native hosted lifecycle services own startup work.

## Installation

```sh
dotnet add package NOF.Hosting
```

## Usage and boundaries

- [native-hosting](../design/native-hosting.md)
- [handler-registration](../design/handler-registration.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
