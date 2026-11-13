---
description: Add native service registration, HTTP composition or startup work
---

# Extend Native Host Composition

Use IServiceCollection registrations directly. Use IHostedService or IHostedLifecycleService
for startup tasks; HTTP middleware and IHttpEndpointModule mapping belong after native Build.
There is no NOF initialization step, topology protocol, host wrapper or custom build path.
See docs/en/design/native-hosting.md and its Chinese edition for current APIs and migration.
