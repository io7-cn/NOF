# Interactive Sample capability workbench

[简体中文](../../zh-CN/design/sample-workbench.md) · [RabbitMQ routing](rabbitmq-sample.md) · [Authorization](authorization.md)

The Sample is a local capability demonstration, not an identity server. AppHost runs the producer, Notifications receiver, Keycloak, RabbitMQ, PostgreSQL and Garnet. The workbench calls real endpoints and displays stored delivery state and receiver projections. It sends no real email.

## Start locally

Start Docker Desktop. Use the pinned .NET SDK and installed workloads, then run from the repository root:

```powershell
dotnet build sample/NOF.Sample.AppHost -c Release
dotnet run --project sample/NOF.Sample.AppHost -c Release --no-build --launch-profile http -p:AspireUseCliBundle=false
```

On this repository's Windows development machine, run dotnet commands elevated. The HTTP profile is exclusively for local development. Open the dashboard login URL printed by AppHost; its access token changes per launch. Wait for all resources to become healthy, then open [the workbench](http://localhost:55892/demo.html).

If using the Aspire CLI instead, ensure it builds/starts the current configuration; do not use an old Debug AppHost after building Release. The CLI may create a local aspire.config.json, which is not required project source. The native launch above disables that CLI trampoline.

| Service | Local address / parameters |
|---|---|
| Sample workbench | http://localhost:55892/demo.html |
| Configuration CRUD | http://localhost:55892/ |
| Streaming | http://localhost:55892/streaming-demo |
| Permission UI | http://localhost:55892/permissions-demo |
| Notifications | http://localhost:55893/api/messages/receipts |
| Keycloak | http://localhost:18080, realm `nof` |
| Keycloak administration | `nof-admin` / `nof-admin-local` |
| RabbitMQ management | http://localhost:15672, `guest` / `guest` |
| Aspire dashboard (HTTP profile) | http://localhost:18888, use printed login token |

Keycloak imports `sample/NOF.Sample.AppHost/Realms/nof-realm.json` when its development container starts. The browser client is `nof-sample-web`, redirect URI `http://localhost:55892/signin-oidc`; API audience is `nof-sample`. Cookie/OpenIdConnect and JwtBearer are native ASP.NET Core integrations. The client secret and accounts are checked-in local demo credentials, not deployment settings. If changing the producer port, update the realm's redirect/logout/origin URLs too.

## Authorization scenarios

Use **Log in to Keycloak** and **Log out / switch account** on the workbench. Full provider sign-out is required to switch users; opening login again can reuse its current SSO session.

| Identity | Password | Read | Manage |
|---|---|---|---|---|
| Anonymous | — | 401 | 401 |
| alice | `AliceDemo!2026` | 200 | 200 |
| bob | `BobDemo!2026` | 200 | 403 |
| charlie | `CharlieDemo!2026` | 403 | 403 |

These are actual Contract policies, not page-only visibility checks. Keycloak's `entitlements` claim supplies grants. The permission UI demonstrates AuthGuard independently of UseCase declarations. Server-rendered components copy the native authentication principal into the scoped UI identity. Their local operation calls use the thread pool so the Sample's synchronous domain-event publishing cannot block the Blazor synchronization context while a handler awaits database work.

## Hands-on capability coverage

| Entry point | What to try / observe |
|---|---|
| Configuration center | Create a root and child, add a JSON file, activate it, inspect preview and delete test data. Duplicate/invalid input and deleting a parent with children demonstrate declared failures. Value objects, generated DTO mapping, native EF aggregate/read ports, local domain events and the unit of work participate in the same flow. |
| Outbox and messaging | Pause delivery, submit an order with `demo@example.test`, inspect two persisted intents, resume and wait for three receiver projections. Expand complete responses to inspect immutable context/headers. |
| Retry | Click fail-next, then submit. One intent records an unsuccessful attempt and later becomes Sent with an increased attempt count. Previous failure text remains visible as history. |
| Business idempotency | Repeat delivery for the selected order. New transport IDs still produce only three projections because the receiver enforces tenant-scoped business uniqueness. |
| Tenant cache | Write under host, read under tenant-a (absent), return to host (present). The logical key is the same. Garnet implements the Redis cache provider. |
| Object storage | Save/read/delete the fixed local demonstration object. |
| Value objects and IDs | Validate blank and valid names; inspect user-provided validation messages. Generate five Snowflake IDs. |
| Local events | Publish text and see the automatically discovered LocalEventHandler's recorded event. |
| Invocation filters | Refresh real operation duration/origin/tenant/trace records. Dashboard traces/logs cover both services and native clients. |
| Streaming | Start SSE, observe incremental events, cancel and exercise the expected-failure response. |
| OpenAPI | Open native HTTP endpoint descriptions; Contract contains no transport routing. |

An order saves two transport-independent intents: SendEmail routes to a queue, OrderCreated to a topic exchange with two queue bindings. Outbox handlers in the producer's infrastructure choose routes and use the integration's concrete RabbitMQMessageClient. The receiver maps queues directly to operations; CountOrder needs no public Contract method. No framework Inbox is involved. ACK follows execution; handlers own commit and idempotency. The mail projection simulates an effect, not real SMTP.

Pause/fail-next controls and `/api/demo` diagnostic endpoints exist only in Development. Pausing affects this process's sample Outbox handlers, not RabbitMQ itself, and does not cancel an already-started send. Resume delivery before leaving the environment unattended. Filters/events are bounded in-memory observations; Outbox and business projections are durable. These administrative demo controls are not a production authorization design.

## Existing development databases

MigrateOnStart applies reviewed EF migrations to the local producer database. The guarded RemoveInbox migration refuses unresolved legacy work. The ExternalIdentityProvider migration removes old identity entities from the model while preserving physical identity tables. Back up and review each tenant database before upgrading a deployed environment.

Notifications uses EnsureCreated only for a fresh local SQLite file. For an old Receipts table without IdempotencyKey, stop the receiver and back up its database first. Review `Persistence/UpgradeBusinessIdempotency.sql`; run it once with an error-stopping SQLite client, for example `sqlite3 -bail notifications.db ".read Persistence/UpgradeBusinessIdempotency.sql"`, then restart the receiver. It derives order keys and legacy row keys without deleting history. Unresolved duplicate order/kind/tenant rows fail index creation and roll back when the client exits; review duplicates explicitly. Never delete a populated receiver database as an upgrade shortcut.

## Test isolation

Optional broker tests use NOF_SAMPLE_RABBITMQ. Point them at a separate RabbitMQ virtual host, not the running demo's default virtual host: tests use the same queue names and would compete with its consumers. Native callback, schema-upgrade, authorization and projection idempotency tests also run without an external broker. Browser tests exercise the real local provider and services; they do not establish production identity-provider conformance.
