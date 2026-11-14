# Native host lifecycle

The former registration/initialization step pipelines and topology contracts are removed.
Register services directly with IServiceCollection. Compose HTTP middleware and endpoint
modules after native Build. Use IHostedService/IHostedLifecycleService for startup work.

See the [English migration guide](../en/design/native-hosting.md) or
[简体中文](../zh-CN/design/native-hosting.md).
