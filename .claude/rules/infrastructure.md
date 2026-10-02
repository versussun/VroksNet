---
paths:
  - "src/VroksNet.Infrastructure/**"
---

# VroksNet.Infrastructure

The Clean Architecture Infrastructure layer. It implements Application's interfaces: persistence (EF Core, repositories), parsers, external service clients, file system. It references `VroksNet.Application` (and transitively Domain).

## Rules

### Persistence (SQLite + EF Core)

- **One `DbContext`:** `VroksNet.Infrastructure.Persistence.VroksNetDbContext`. Always resolve it through `IDbContextFactory<VroksNetDbContext>`, never by direct injection, so every read or write gets its own short-lived context.
- **Serialize all writes through `IDbWriteQueue`.** Call sites (e.g. `ApiSpecificationRepository`) enqueue a delegate, and the single `DbWriteBackgroundService` executes them one at a time. **Never add a write path that bypasses the queue.** This guards against concurrent writes between the Mock API and the async publish worker (`docs/project-brief.md` §3).
- **Connection setup lives in `InfrastructureServiceCollectionExtensions`.** `AddInfrastructure(...)` configures the connection with a 5s `busy_timeout`. `InitializeDatabaseAsync()`, called once at ApiService startup, runs migrations plus `PRAGMA journal_mode=WAL`.
- **`CallRecord.Timestamp` is stored as UTC ticks (`INTEGER`)** through a value converter in `VroksNetDbContext`. The SQLite provider can't `ORDER BY` or compare `DateTimeOffset`, and EF's default ISO text would order wrongly across offsets anyway. Apply the same treatment to any other `DateTimeOffset` you need to sort or filter on in SQL. Changing an existing column's storage needs a data-converting migration (see `AddCallHistoryIndexesAndRequestLine`), not just the generated `AlterColumn`.
- **Keep the unique index on `ApiSpecification.Title`** (`VroksNetDbContext.OnModelCreating`). `ApiSpecificationRepository.UpsertAsync` relies on it: it deletes the existing row with the same `Title` (cascading to its `MockEndpoint`s) and inserts fresh. Don't switch to attaching/patching a detached graph, which threw `DbUpdateConcurrencyException`.
- **Add migrations from the repo root:**

  ```
  dotnet ef migrations add <Name> --project src/VroksNet.Infrastructure --startup-project src/VroksNet.ApiService --output-dir Persistence/Migrations
  ```

  `Microsoft.EntityFrameworkCore.Design` must be referenced by **both** Infrastructure (the model) and ApiService (`dotnet ef` requires it on the startup project).

### Storage modes

- **In-memory by default.** When `ConnectionStrings:VroksNetDb` isn't set, the app uses `Data Source=VroksNetInMemoryDb;Mode=Memory;Cache=Shared`: named and shared-cache, **never** bare `:memory:`. With `:memory:`, every factory-issued connection would get its own empty database.
- **Keep `InMemoryDatabaseKeepAlive`.** It is a singleton, resolved eagerly at the top of `InitializeDatabaseAsync` before migrations, and holds one connection open for the app's lifetime. Without it the shared-cache DB is dropped when the first short-lived connection closes.
- **File mode = set `ConnectionStrings:VroksNetDb`** (config, `ConnectionStrings__VroksNetDb` env var, or user secrets) to a file path. That value is the only switch; don't add a separate flag.
- **In file mode, `AddInfrastructure` creates the missing parent directory** (`Directory.CreateDirectory` on the `SqliteConnectionStringBuilder.DataSource` directory). SQLite creates the `.db` file itself (`ReadWriteCreate`) but never missing directories.
- **Docker uses file mode.** The image sets `ConnectionStrings__VroksNetDb=Data Source=/app/data/vroksnet.db` with `VOLUME /app/data`. Local `dotnet run` gets the in-memory default.
- **Never add a live "switch storage" or "restart now" button.** The Settings page's `Storage` section only builds the `ConnectionStrings__VroksNetDb` value and copies it to the clipboard. `IDbContextFactory` is wired once at startup, so switching modes means setting the env var and restarting, and the app has no supervisor to bring itself back up outside a Docker restart policy.

### Spec parsing

- **OpenAPI:** use `Microsoft.OpenApi` + `Microsoft.OpenApi.YamlReader` (the 3.x line). **Register the YAML reader explicitly:** `new OpenApiReaderSettings().AddYamlReader()` (from `Microsoft.OpenApi.Reader`), then `OpenApiDocument.LoadAsync(stream, "yaml", settings, cancellationToken)`. Without it you get `NotSupportedException: Format 'yaml' is not supported.`
- **AsyncAPI** (`AsyncApiSpecificationParser`): NuGet has no typed AsyncAPI model, so the parser walks the raw YAML via `SharpYaml` (`YamlMappingNode`/`YamlSequenceNode`/`YamlScalarNode`) and resolves the spec's own local `"#/a/b/c"` `$ref`s by hand. Keep that minimal: no general JSON Reference or external-file support.
- **Pin `SharpYaml` to the exact version `Microsoft.OpenApi.YamlReader` brings transitively** (currently `2.1.5`), so it can't drift. Don't add a second general-purpose YAML library.
- **AsyncAPI operation keys use the format `"{channel address}:{action}"`** (e.g. `"orders.created:send"`, per `MockEndpoint.OperationKey`). They are **not** invokable through `/mock/{**path}`, because `MockInvocationEndpoints`/`OperationKeyMatcher` assume HTTP method + path. Publishing them is Phase 03 work (see "Message brokers" in `.claude/rules/apphost.md`).
- **Keep the two parser interfaces separate.** `ISpecificationParser` (→ `OpenApiSpecificationParser`) and `IAsyncApiSpecificationParser : ISpecificationParser` (→ `AsyncApiSpecificationParser`) are registered side by side. The derived interface adds no members; it exists so DI can tell the two apart (otherwise the last registration silently wins).
- **Each spec kind is its own vertical slice:** its own `Application/Specifications/Import{Kind}Spec` request + handler and its own `POST /api/specifications/{openapi|asyncapi}` endpoint, with no `Kind` parameter branching. Follow this pattern for any new spec kind.

### Connection testing

- **`IConnectionTester` → `ConnectionTester`** (`POST /api/connections/{id}/test`) actually reaches the stored `Connection`'s target instead of only validating its shape.
- **Don't reuse the Aspire-wired broker clients** (`AddRabbitMQClient`/`AddNatsClient`). They point at the dev-time AppHost resources, while a `Connection` holds arbitrary user-typed host and credentials. Open clients directly:
  - `Http`: a short-timeout `HttpClient` GET. Any HTTP response counts as reachable, even a 4xx; only a thrown exception counts as failure.
  - `RabbitMq`: `RabbitMQ.Client` `ConnectionFactory.CreateConnectionAsync`.
  - `Nats`: `NATS.Client.Core` `NatsConnection.PingAsync`.
- **Reference `RabbitMQ.Client` and `NATS.Client.Core` directly**, not the `NATS.Net`/`Aspire.NATS.Net` meta-packages, which pull in JetStream/KV/ObjectStore/Hosting. Pin them to the exact versions the Aspire client packages already resolve.
- **All branches share one 5s timeout and return `ConnectionTestResult(bool Success, string Message)`.** Make `Message` safe to show verbatim in the UI: never a raw connection string or a full stack trace.
- `MessageSender` (Test Scenarios) follows the same branching and clients, but publishes or POSTs for real. It appends the operation's path to the connection URL's own base path and keeps its query string (`https://host/v1?api-key=x` + `/pets` → `/v1/pets?api-key=x`). `MessageSenderUrlTests` covers this. Don't go back to `new Uri(baseUri, path)`, which drops both. See `.claude/rules/application.md`.

- **Open RabbitMQ connections only through `RabbitMqConnections.OpenAsync`.** It sets the client's own timeouts, disables automatic recovery and *cancels* the attempt on timeout. Don't go back to `CreateConnectionAsync(...).WaitAsync(timeout)`: that only abandons the attempt, which can still open later and stay alive through recovery for the life of the process.
- **`MessageListener` (Listen-mode Test Scenarios) must never take messages from a channel's real consumers.**
  - RabbitMQ: a server-named exclusive auto-delete queue, bound to the scenario's exchange (default `amq.topic`). Never consume the channel's own queue. On a fanout/headers exchange the binding key is ignored, so any message on the exchange counts.
  - NATS: a core subscription, then `PingAsync` so the subscription is registered before the listen window starts.
  - Both subscribe with `TestScenarioListening.SubscriptionPatternOf(channel address)`, where whole-segment `{param}`s become `*`.
  - Everything lives only for one run. Connect/setup (10s) is separate from the listen timeout. Each stage reports its own message, and a missing exchange reports a readable one.
  - Time the listen wait out by cancelling the read, not via `WaitAsync`: an abandoned NATS read faults unobserved when the subscription is disposed.

### Response templating

- **`IResponseTemplateEngine` → `VroksNet.Infrastructure.Templating.ResponseTemplateEngine`**, a singleton built with `TimeProvider.System` (for `{{now}}`). The rules (3.9–3.10 in `docs/contract-testing-plan.md`) live in its doc comment: string-literal tracking decides escaping, an unfillable placeholder becomes `null`/empty plus a warning, and `{{…}}` that isn't `uuid`/`now`/`request.…` is left alone.
- **`{{request.body.<jsonpath>}}` uses `JsonPath.Net`**, pinned in `Directory.Packages.props` to the version that shares `Json.More.Net` with `JsonSchema.Net` (2.1.1 ↔ 7.3.4). Bump them together, and reference it only from the engine.
- **`OpenApiSpecificationParser` sets `ParsedOperation.ExampleStatusCode`:** the status of the response the example came from (`"2XX"` → 200), otherwise the lowest declared 2xx, otherwise null. Endpoints imported before it was tracked keep a null `MockEndpoint.ExampleStatusCode` and answer 200 until the spec is re-imported.

### Contract-testing schema foundation

- **The full plan is in `docs/contract-testing-plan.md`.** Don't wire up validation against these schemas without updating that plan too. Phases A (foundation) and B (Test Scenario HTTP response validation) are done.
- **`ISchemaValidator` → `VroksNet.Infrastructure.SchemaValidation.SchemaValidator`** validates JSON against a JSON Schema using `JsonSchema.Net`. That library is pinned in `Directory.Packages.props` and referenced only here.
- **Both parsers extract request/response schemas at import time** and store them on `MockEndpoint.RequestSchema`/`ResponseSchema`. For AsyncAPI, the message payload reuses `ResponseSchemaJson`. Schemas are self-contained:
  - OpenAPI inlines local `$ref`s via `IOpenApiSchema.SerializeAsV31(...)` with `OpenApiWriterSettings { InlineLocalReferences = true }`.
  - AsyncAPI follows one manual `$ref` hop.

  Schemas are stored instead of re-parsed on demand for the same reason `ExampleTemplate` stores the example.
- **OpenAPI also stores every declared response's schema** in `MockEndpoint.ResponseSchemasByStatus`, keyed `"200"`/`"4XX"` (range keys upper-cased)/`"default"`, value `null` when that response declares no JSON body. It's one JSON column via a value converter in `VroksNetDbContext`, defaulting to `"{}"` for rows that predate it. Never let that default become `""`: it wouldn't deserialize, and every read of an old endpoint would throw.
- **Only `RunTestScenarioHandler` consumes these schemas so far.** It validates an Http run's response against `ResponseSchemasByStatus` and a Listen run's message against `ResponseSchema` (the AsyncAPI payload). They aren't exposed via `MockEndpointDetail`, the API or the UI.
