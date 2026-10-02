# Contract Testing — implementation plan

**Status:** all phases A–H are implemented — all four test types work, the mock answers with a templated example at the status from the spec, Send scenarios publish to a configurable RabbitMQ exchange, and the provider port supports CORS by configuration.
**Context:** the detailed breakdown of Phase 04 of `docs/project-brief.md` ("Contract testing + polish"). That document captures *what* and *why* at the product level; this one covers *how*, at the level of the domain model, components and implementation steps. Code structure and style rules are, as usual, in `.claude/CLAUDE.md`.

## 1. The four kinds of tests

According to the customer, four different scenarios are needed. Below is what they're called in contract testing generally (Microcks, Pact and so on), so we don't invent terms along the way.

| # | User's description | Term | Direction | Validation |
|---|---|---|---|---|
| 1 | We send an HTTP request to a service and check that the response matches the spec | **Consumer test (request/response)** | us → service (HTTP) | the **response** body against the operation's response schema |
| 2 | We send a message to a broker — just send it, that's all | **Producer test (fire-and-forget)** | us → broker | none (only that it was delivered/published) |
| 3 | A service sends us an HTTP request on a path from the spec, we answer per the spec, and check that their request matches the spec | **Provider test (request/response)** | service → us (HTTP) | the **incoming request** body against the operation's request schema |
| 4 | We wait for a message on a broker and validate it against the schema | **Provider test (async, listen)** | broker → us | the message body against the AsyncAPI payload schema |

An important structural difference within the four: **#1, #2 and #4 are on-demand tests** (the user presses "Run" and gets one result per run). **#3 is a passive/permanent mode**: once a mock is switched into this mode, every real incoming call to it is logged and validated automatically — it's not "one Run button" but a state toggle on the mock endpoint. This should shape the UX and the data model (see section 4).

## 2. What's already implemented

### 2.1 Shared infrastructure (reused by all four types)

- **`ApiSpecification`/`MockEndpoint`** (`VroksNet.Domain`) — the spec's parsed operations, `OperationKey` (`"GET /pets"` for OpenAPI, `"orders.created:send"` for AsyncAPI — see `VroksNet.Domain.TestScenarios.OperationCompatibility`), `ExampleTemplate`.
- **`Connection`/`ConnectionServiceType`** — named connections (Http/RabbitMq/Nats), CRUD on the Settings page, `IConnectionTester`/`ConnectionTester` — a reachability check (not to be confused with contract validation).
- **`TestScenario`** (`VroksNet.Domain.TestScenarios`) — a saved "operation X of a spec → Connection Y" scenario, CRUD + `RunTestScenarioHandler`, UI on the Test Scenarios page.
- **`IMessageSender`/`MessageSender`** (`VroksNet.Infrastructure.Connections`) — the actual send: an HTTP request (for an `Http` connection) or a publish to RabbitMQ/NATS (for a `RabbitMq`/`Nats` connection). It doesn't reuse the Aspire dev resources from `AppHost.cs` — it opens a connection from the data entered in `Connection.Value`.
- **`CallRecord`/`CallDirection`** — the call-history entity. `CallDirection` already includes `InboundHttpRequest`, `OutboundBrokerPublish` and `OutboundHttpRequest`. Since Phase E the history can be read (`GET /api/call-records`, the Call History page), and both sources write it: `RunTestScenarioHandler` (`OutboundHttpRequest`/`OutboundBrokerPublish`) and `InvokeMockEndpointHandler` (`InboundHttpRequest`).

### 2.2 Type 1 (Consumer, request/response) — ✅ implemented (Phase B)

`TestScenario` + `RunTestScenarioHandler` + `MessageSender` (the HTTP branch) send the operation's request through an Http connection, get the status and body, and check the response against the schema the spec declares for the **received** status. A response that doesn't match the spec fails the run even if the HTTP request succeeded. Details in 4.2.

### 2.3 Type 2 (Producer, fire-and-forget) — fully implemented

`TestScenario` + `MessageSender` (the RabbitMq/Nats branch): `ConnectionFactory.CreateConnectionAsync` + `BasicPublishAsync` for RabbitMQ, `NatsConnection.PublishAsync` for NATS. We don't wait for a reply — matching the description "send it and that's all". Validation isn't needed here by definition (fire-and-forget), so no further work is required.

### 2.4 Type 3 (Provider, request/response) — ✅ implemented (Phase D)

An operation with "Serve at real path" switched on answers at its **real path** on a separate provider port (the real service is pointed there by changing host:port); the incoming request body is checked against `RequestSchema`, and the result goes into the history. `/mock/...` still answers for every enabled operation (and now validates the body too). Details in 4.4.

### 2.5 Type 4 (Provider, async listen) — ✅ implemented (Phase C)

A `TestScenario` in `Listen` mode waits for the next message on the operation's channel through `IMessageListener` (RabbitMQ/NATS) and validates it against the AsyncAPI payload schema. Details in 4.3.

### 2.6 Operation schemas — ✅ closed by Phase A

The parsers used to see the full JSON Schema, but `ParsedOperation`/`MockEndpoint` kept only `ExampleJson`/`ExampleTemplate` and threw the schema away — without it, none of Types 1/3/4 could be validated. Now the schemas are extracted and stored in `MockEndpoint.RequestSchema`/`ResponseSchema` (for AsyncAPI the payload schema lives in `ResponseSchema`), and there's an `ISchemaValidator`. Details in 4.1. Type 1 already uses them (Phase B, with the schemas of every status — see 4.2); Types 3/4 — Phases C–D.

## 3. Open decisions (to confirm before implementation)

1. ~~**JSON Schema library.**~~ **Decided and done (Phase A):** [`JsonSchema.Net`](https://github.com/gregsdennis/json-everything) 7.3.4, pinned in `Directory.Packages.props`, used only inside `SchemaValidator` (`VroksNet.Infrastructure.SchemaValidation`) — it leaks out only through `ISchemaValidator`.
2. ~~**Where to store the schema.**~~ **Decided and done (Phase A):** at import time — `MockEndpoint.RequestSchema`/`ResponseSchema` (nullable `string`, migration `AddMockEndpointSchemas`), filled by `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` from `ParsedOperation.RequestSchemaJson`/`ResponseSchemaJson`. For AsyncAPI, `ResponseSchemaJson` carries the message payload schema (the decision on "one field instead of three" below — see 4.1).
3. ~~**Data model for "Listen" (Type 4).**~~ **Decided (Phase C):** `TestScenario` gets a `Kind` field — `Send` (an HTTP request or a publish, as before) / `Listen` — plus `ListenTimeoutSeconds` and `ListenExchange` (since Phase G, a shared `Exchange`, see 4.7). HTTP vs broker is still determined by the connection type; separate `SendHttp`/`PublishBroker` kinds aren't needed. A new scenario's default mode comes from the operation's `action` (see 4.3), and the user can switch it; existing scenarios stayed `Send`.
4. ~~**Model for the passive Provider mode (Type 3).**~~ **Decided (Phase D):** one flag, `MockEndpoint.ServeAtRealPath`, per operation (a toggle on the operation card), plus "Serve all / Stop serving" for the whole spec. There's no separate flag for validation — the incoming body is always validated when there's a schema.
5. ~~**Real-path collisions.**~~ **Decided (Phase D):** (a) there are no collisions with `ApiService`'s own routes at all — the provider lives on a **separate port** where only the mock is reachable; no guard list is needed. (b) Between operations — **refused when switching on**: an operation that could accept the same request as an already-enabled one can't be switched on (same method, same path length, segments equal or at least one is a parameter: `GET /pets/{id}` overlaps `GET /pets/mine`). The refusal names the conflicting operation and spec.
6. ~~**History/reporting.**~~ **Decided (Phase E):** the read side of `ICallRecordRepository`, `GET /api/call-records` and the Call History page — see 4.5.
7. ~~**Storing the validation result in `CallRecord`.**~~ **Decided:** fields on the record itself — `bool? ContractValid` (`null` = there was no schema / validation didn't apply) + `string? ValidationErrors` (a JSON array of messages from `SchemaValidationResult`). Shared by Types 1/3/4. No separate errors table — errors are only ever read together with the record. The fields were added by Phase B's migration (`AddResponseSchemasByStatusAndCallRecordContract`, together with `StatusCode`/`TestScenarioId` from 4.5) — Type 1 already fills them, and Phases C/D will fill them the same way.

Decisions for deferred phases (to close before the corresponding phase starts):

8. ~~**(Phase F) The mock's response status.**~~ **Decided and done (Phase F):** the status of the response the example came from (`"2XX"` → 200); if the example came from the request body or there's none — the lowest declared 2xx; otherwise (only `default`, or a spec imported before Phase F) — 200. Stored in `MockEndpoint.ExampleStatusCode`. No body is sent for 204/304.
9. ~~**(Phase F) An unresolvable placeholder.**~~ **Decided and done (Phase F):** `null` outside a JSON string literal, an empty string inside one (and in a non-JSON template); each such placeholder becomes a warning in `CallRecord.Warnings`, and the response doesn't break.
10. ~~**(Phase F) The type of the substituted value.**~~ **Decided and done (Phase F):** inside a string literal the value is JSON-escaped as text (a body node that isn't a string becomes its JSON text); outside quotes a body node is inserted as JSON as-is, and a path/query/header/`uuid`/`now` value as a bare number/`true`/`false`/`null` if it reads as one, otherwise as a quoted string (the result stays valid JSON). A non-JSON template (not starting with `{`, `[` or `"`) gets plain text substitution.
11. ~~**(Phase G) One exchange field or two.**~~ **Decided and done (Phase G):** one shared `TestScenario.Exchange` (renaming the `ListenExchange` column) — Send publishes to it, Listen binds to it.
12. ~~**(Phase G) Default exchange for Send.**~~ **Decided and done (Phase G):** `null` on Send means the default exchange `""` (as before), on Listen — `amq.topic`. Existing scenarios are untouched: they have `null` and behave the same, so no separate data migration was needed. New RabbitMQ scenarios are created in the UI with `amq.topic` in the Exchange field, so Send and Listen meet by default; clearing the field on Send publishes straight into the queue.
13. ~~**(Phase H) Which origins to allow on the provider port.**~~ **Decided and done (Phase H):** off by default; `Provider:CorsOrigins` is a comma-separated list, `*` means any origin. No credentials.

## 4. Target architecture

### 4.1 Phase A — foundation: schemas + validator (no behavior change) — ✅ implemented

- `ParsedOperation` (`ISpecificationParser`) is extended with schemas (both new parameters default to `null`, so existing construction sites don't break):
  ```
  ParsedOperation(string OperationKey, string? ExampleJson, string? RequestSchemaJson = null, string? ResponseSchemaJson = null)
  ```
  `OpenApiSpecificationParser` takes `RequestSchemaJson` from `requestBody.content["application/json"].schema` and `ResponseSchemaJson` from the schema of the first JSON response (the same response the example comes from) — both serialized via `IOpenApiSchema.SerializeAsV31(...)` with `OpenApiWriterSettings { InlineLocalReferences = true }`, so local `$ref`s to `#/components/schemas/...` are expanded into the schema's own text (otherwise the schema isn't valid on its own outside the context of the whole document). `AsyncApiSpecificationParser` puts the message payload schema into `ResponseSchemaJson` (decision made — no third field under an HTTP-neutral name), resolving one `$ref` hop into `components.schemas.*` by hand the same way (as was already done for the example).
- `MockEndpoint` got `RequestSchema`/`ResponseSchema` (nullable `string`) — migration `20260909105919_AddMockEndpointSchemas`. `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` copy them from `ParsedOperation`. **Not exposed anywhere yet** (not in `MockEndpointDetail`/the API/the UI) — that's by plan; behavior doesn't change until Phase B.
- `ISchemaValidator`/`SchemaValidationResult` in `Application/Abstractions`; the implementation is `VroksNet.Infrastructure.SchemaValidation.SchemaValidator` on `JsonSchema.Net` (`EvaluationOptions { OutputFormat = OutputFormat.List }`, to get a flat list of error messages rather than just "failed").
- Tests: `OpenApiSpecificationParserTests`/`AsyncApiSpecificationParserTests` gained checks that the schema is extracted and `$ref` really is expanded (`Assert.DoesNotContain("$ref", ...)`); a new `SchemaValidatorTests` — pure unit tests of the validator (valid/invalid instance, wrong type, a `minimum` violation, broken JSON) — without Docker, like the rest of that kind of infrastructure (`ConnectionTesterTests`/`MessageSenderTests`).

### 4.2 Phase B — Type 1: response validation on Send — ✅ implemented

Decisions made:
- **The schema is chosen by the response's actual status**, not one "response schema" per operation. Otherwise a 404/500 with its own (correct) schema would be validated against the 200 schema and produce false errors.
- **A run's outcome = transport AND contract.** `Success`/`LastRunSuccess` = `false` if the response doesn't match the spec. `ContractValidation` shows what exactly failed.

Implementation:
- `OpenApiSpecificationParser` collects the schemas of **all** declared responses into `ParsedOperation.ResponseSchemasByStatus`: the key is the OpenAPI status key (`"200"`, `"4XX"` (range keys upper-cased), `"default"`), the value is a self-contained JSON schema of the body, or `null` if the response is declared without a JSON body. Stored in `MockEndpoint.ResponseSchemasByStatus` (one JSON column via a value converter). `ResponseSchema` stays as it was (the first JSON response / the AsyncAPI payload) — for Phases C/D.
- The selection rule is in Domain, `MockEndpoint.TryGetDeclaredResponse(statusCode, out schema)`: exact code → range (`"2XX"`) → `"default"`, as in the OpenAPI specification.
- `MessageSendResult` got `int? StatusCode`; `MessageSender` fills it for HTTP.
- `RunTestScenarioHandler` (which takes `ISchemaValidator`), for a successful HTTP response:
  - no declared responses (AsyncAPI, or an OpenAPI spec imported before Phase B) → validation is skipped (`ContractValidation = null`). **Old specs must be re-imported** for validation to work on them;
  - the status isn't declared in the spec → a contract violation (`"Status 500 isn't declared for GET /pets in the spec."`);
  - the status is declared without a JSON body → the contract holds, the body isn't checked;
  - a schema is declared but the body is empty → a violation; otherwise → `ISchemaValidator.Validate(schema, body)`.
- `RunTestScenarioResult` got `StatusCode` and `ContractValidation` (`SchemaValidationResult?`). On a violation, `Message` is extended with "— response doesn't match the spec (N violation(s))".
- `CallRecord` fills `StatusCode`, `TestScenarioId`, `ContractValid` and `ValidationErrors` (a JSON array).
- UI (Test Scenarios): under a Run result — a separate "Matches spec" / "Contract violated" badge with the list of errors (`role="status"`, `aria-label="Contract check"`).
- Tests: unit — the handler (match, violation, its own status's schema, range/default, undeclared status, status without a body, empty body, no declared responses, send failure), the parser (fixture `response-statuses-openapi.yaml`), a dictionary round trip through real SQLite; integration — a run against ApiService's own `GET /api/connections` with an array schema (passes) and an object schema (violation); E2E — the "Contract violated" badge on the Test Scenarios page.

### 4.3 Phase C — Type 4: Listen & validate — ✅ implemented

Decisions made:
- **The mode is an explicit `TestScenario.Kind` field, defaulting by `action`** (see 3.3). An AsyncAPI operation with `action: send` is what the described service itself publishes, so testing it means listening (`Listen`); `action: receive` (the service consumes) and HTTP mean sending (`Send`). The rule is `TestScenarioListening.DefaultKindFor` in Domain; the server exposes it as `MockEndpointDetail.DefaultTestScenarioKind`, and the form fills it in when an operation is picked.
- **RabbitMQ is listened to through our own temporary queue on an exchange**, not by reading the channel's queue: a server-named exclusive + auto-delete queue, bound to the exchange with binding key = the channel address. Real consumers get their own copy; nothing is "stolen". The exchange is set on the scenario (`Exchange`, before Phase G — `ListenExchange`), default `amq.topic`. Consequence: the service must publish to the exchange — a message sent straight into a queue through the default exchange can't be overheard this way.
- NATS — a plain core subscription, followed by a `PING`, so the subscription is surely registered before the wait starts.
- **Channel parameters.** The subscription uses a pattern: an address segment that is entirely an AsyncAPI parameter (`orders.{region}.created`) becomes the `*` wildcard (the same in RabbitMQ topic and NATS). If a parameter is only part of a segment, or the address is `/`-separated (`user/{id}/signedup`), it can't be subscribed to: such a scenario isn't saved (`CanListen` = false). On a fanout/headers exchange the binding key is ignored — any message on the exchange counts.
- ~~**Limitation:** Listen doesn't hear our own Send on the same operation.~~ Lifted by Phase G (4.7): Send publishes to the scenario's exchange, and a Send/Listen pair on one exchange meets.

Implementation:
- `IMessageListener.ListenAsync(connection, operationKey, timeout, exchange)` → `MessageListenResult(Received, Message, Payload)`; `MessageListener` (Infrastructure) is short-lived — the connection/queue/subscription live only for the run. Connecting and setting up the subscription (10s) are separate from the wait timeout, and each stage has its own message; a missing exchange gives a readable error. RabbitMQ connections are opened through the shared `RabbitMqConnections` (client timeouts, no auto-recovery, cancellation by token) — now also used by `ConnectionTester`/`MessageSender`, since a connection abandoned on timeout used to leak.
- Wait timeout: 1–80 seconds, default 30 (`TestScenarioListening`). The upper bound keeps a run (the wait + up to 10s to connect and set up), which holds the HTTP request open, within the Admin UI's HttpClient timeout (100s).
- `RunTestScenarioHandler` branches on `Kind`: `Listen` → the listener → (if received and the operation has a payload schema) `ISchemaValidator`. Nothing received within the timeout — the run fails without validation. An empty message when a schema is declared — a violation.
- A new `CallDirection.InboundBrokerMessage`; the history record stores the received message (or the reason for failure) in `ResponseSnapshot`.
- Validation on create/update: an unknown `Kind` is rejected; `Listen` only for operations with a subscribable channel, the timeout in range; the exchange is stored only for a RabbitMQ connection; for `Send` the listen settings are dropped. Migration `AddTestScenarioListenMode`.
- The UI rules (`RequiresHttpConnection`, `CanListen`, `DefaultTestScenarioKind`) are provided by the server in `MockEndpointDetail` — Web doesn't derive them from the operation key itself.
- `ChannelAddressOf` (the channel address from an operation key) moved from `MessageSender` into Domain (`OperationCompatibility`) — shared by sending and listening.
- UI (Test Scenarios): for an AsyncAPI operation — a mode choice; in Listen mode, instead of a payload — "Wait up to (seconds)" and (for a RabbitMQ connection) "Exchange". In the table — a "Listen" badge, during a run — "Listening…". In Call History — "Broker message (in)".
- Tests: unit — the default mode, validation on create, the Listen branch (received/violation/timeout/defaults), `MessageListener` against unreachable brokers; integration — real RabbitMQ (via `amq.topic`, a valid and an invalid message) and NATS, the timeout; E2E — the form suggests Listen for a `send` operation, and a run with a 1s timeout reports "No message".

### 4.4 Phase D — Type 3: Provider mode (real path + validating incoming calls) — ✅ implemented

Decisions made — see 3.4 and 3.5: a separate port, a flag per operation, overlaps refused.

Implementation:
- **The provider port.** `ApiService` listens on an additional port when `Provider:Port` (env `Provider__Port`) is set: AppHost — the `provider` endpoint, pinned to `7353` (`http://localhost:7353`, also `Provider__PublicUrl`); Docker — also `7353` (next to `8080`), so the provider port number is the same everywhere. `ProviderPortSetup.ListenOnProviderPort` *adds* the port to the already-configured addresses (`ASPNETCORE_URLS` or `ASPNETCORE_HTTP(S)_PORTS`) through the pure `ProviderListenAddresses.Merge` (Infrastructure, covered by unit tests); `MapProviderPort` uses `MapWhen` to divert all of this port's traffic to the mock before static files, the SPA fallback and the endpoints. Verified both under Aspire (integration tests) and in the built Docker image.
- **Protection against misconfiguration:** startup fails with a clear error if the provider port matches one of the API's own addresses (otherwise the whole API would silently become the mock), if it's ≤ 0 / > 65535, or if `Kestrel:Endpoints` is set (Kestrel would ignore the added address). If the main addresses are loopback-only (as under AppHost), the provider port also listens only on `localhost` — an unauthenticated mock isn't exposed to the network.
- **`GET /api/system/provider`** → whether the mode is on, the port, and the public URL (if the deployment knows it: AppHost does; behind Docker port mapping it doesn't). The UI shows the operation's full address and disables the toggle if the mode isn't configured.
- **The flag** `MockEndpoint.ServeAtRealPath` (migration `AddMockEndpointServeAtRealPath`), kept on re-import of the spec for operations with the same key.
- **The overlap rule** — `OperationOverlap` (Domain). `SetEndpointProviderMode` refuses (409) a non-HTTP operation and an overlapping one; switching off always succeeds. `SetSpecificationProviderMode` switches on everything it can, in key order (including overlaps within the spec itself — `GET /pets/mine` before `GET /pets/{id}`), and returns the skipped ones with a reason. The check and the write aren't one transaction; a race between two simultaneous switch-ons is acceptable for an admin toggle.
- **API:** `PUT /api/mock-endpoints/{id}/provider-mode` (204/404/409), `PUT /api/specifications/{id}/provider-mode` (what was switched on, what was skipped; 409 if saving failed). The body `{ "enabled": … }` is required — without the field it's a 400, not a silent "switch off". If the write affected fewer rows than expected (the spec was re-imported at that moment) — a 409 "the spec changed, retry", not a false success. "Serve all" doesn't mark operations already being served as skipped. `MockEndpointDetail.ServeAtRealPath`.
- **The mock:** `InvokeMockEndpoint(…, ProviderMode)` — on the provider port only flagged operations answer; in the history, `RequestLine` is the real path (without `/mock`). The request body is validated against `RequestSchema` in both modes; the response doesn't depend on the result (the example template at the status from the spec — see Phase F, 4.6). Not validated: an empty body (the spec doesn't store whether a body is required), a non-JSON body per `Content-Type` (only the `application/json` schema is stored; form data, which the spec also allows, isn't a violation; `*+json` is validated), and a body longer than 64K (truncated JSON would be falsely "invalid"). The body is decoded using the request's `charset`. `HEAD` answers like `GET` (for `curl -I` and probes). CORS on the provider port is off by default — switched on by configuration, see Phase H (4.8).
- **UI:** a "Serve at real path" toggle on the HTTP operation card (on refusal it stays off and shows the reason), "Serve all at real paths" / "Stop serving at real paths" on the specification page with the list of skipped ones.
- Tests: unit — the overlap rule, switching on an operation/spec, the provider answering only for enabled ones, body validation, the flag kept on re-import; integration — port isolation (`/health` there is the mock), the answer at the real path, validation in the history, 409 and "Serve all" with skips; E2E — the toggle persists, an overlapping operation is refused with a reason.

### 4.5 Phase E — Call history — ✅ implemented

Decisions made:
- **Incoming mock calls are logged already now** (gap #3 from 2.4), without waiting for Phase D: otherwise the history page shows only scenario runs. Unmatched calls (404) are written too — "why did the service get a 404?" is exactly a question for the history.
- **Clearing is an explicit "Clear history" button** (`DELETE /api/call-records`, with a confirmation in the UI). There's no automatic limit/rotation.

Implementation:
- `ICallRecordRepository.ListAsync(CallRecordFilter, CallRecordCursor?, limit)` + `DeleteAllAsync`. Filters: spec, operation, scenario, direction, contract outcome. Order — newest first, by (`Timestamp`, `Id`); pagination is **cursor-based (keyset)**, not offset, so new records between requests don't shift pages. `Id` is the tie-breaker for records with the same time.
- **`CallRecord.Timestamp` is stored as UTC ticks (`INTEGER`)**, not the default ISO text: EF Core's SQLite provider can't `ORDER BY`/compare `DateTimeOffset`, and text order is wrong across different offsets. Migration `AddCallHistoryIndexesAndRequestLine` converts existing text values in SQL (via `julianday`) and back in `Down`.
- A new field `CallRecord.RequestLine` — for incoming calls, the actual request line (`GET /mock/pets/1?x=1`); the only way to tell what was requested when nothing matched. Indexes: (`Timestamp`, `Id`) and (`SpecificationId`, `Timestamp`, `Id`) — covering the whole keyset order without a temporary sort.
- **Bodies are capped and not part of the list.** Stored request/response bodies are truncated to 64K characters with a `…(truncated)` marker (`CallRecordSnapshot`); the mock endpoint doesn't read more than that from a request anyway. The history list contains no bodies — `GET /api/call-records/{id}` (`GetCallRecord`) returns them, and the UI loads them on "Details".
- Application: `ListCallRecords` → `CallRecordPage(Items, NextCursor)` with denormalized names (spec/operation/connection/scenario, `"(deleted …)"` for deleted ones) — fetched by `ICallRecordNameResolver` with projections only for the current page's ids, without loading specs with their schemas; `ValidationErrors` parsed into an array; `limit` defaults to 50, max 200; a broken cursor → `ArgumentException` (like the rest of validation — a 500 for now). `ClearCallRecords` → the number deleted.
- `InvokeMockEndpointHandler` writes a `CallRecord`; choosing the response body (the example or `"{}"`) moved from the endpoint into the handler. Logging is **best-effort**: a failure to write the history is logged as a warning, and the mock still answers. For a 404 the history gets the message text, while the client gets it wrapped in ProblemDetails.
- UI: a **Call History** page (`/call-history`, a menu item): filters, a table (time, kind, operation/request line, scenario/connection, status, contract), expandable details (request line, violations, bodies), "Load older", "Refresh", "Clear history".
- Fixed along the way: `MessageSender` lost the connection's base path and query string (`https://host/v1?api-key=…` + `/pets` went to `https://host/pets`) — both are now kept.
- There's no history rotation/limit — only "Clear history"; with active mock use the history grows (up to 128K characters of bodies per record).
- A concern from an early version of the plan, that "a record doesn't appear right after Run", didn't materialize: `IDbWriteQueue.EnqueueAsync` waits for the write to complete, so by the time Run responds the record is already readable.

### 4.6 Phase F — Response dynamics (templating + status from the spec) — ✅ implemented

The mock (both `/mock` and the provider port) answers with the spec's example as a template — the placeholders from `project-brief.md` ("Response dynamics") are filled in with values from the request — and with the status from the spec instead of a fixed 200. Decisions — 3.8–3.10.

- **Placeholders:** `{{request.path.<name>}}`, `{{request.query.<name>}}`, `{{request.header.<name>}}` (name case-insensitive), `{{request.body}}` / `{{request.body.<jsonpath>}}` (`$.a.b` or just `a.b`; several matches — a JSON array), `{{uuid}}` (a new GUID for each occurrence), `{{now}}` (UTC, ISO 8601 `"O"`). Repeated query/header values are joined with commas. An example without placeholders is returned as-is; a `{{…}}` that isn't `uuid`/`now`/`request.…` is left alone.
- **The engine:** `ResponseTemplateEngine` (`VroksNet.Infrastructure.Templating`, a singleton, `TimeProvider` for `{{now}}`) instead of the removed `PassthroughResponseTemplateEngine`. The template is scanned character by character, tracking JSON string literals — that decides whether to escape the value (3.10). JSON path — `JsonPath.Net` 2.1.1 (the same `Json.More.Net` 2.1.1 as `JsonSchema.Net` 7.3.4), pinned in `Directory.Packages.props`, used only inside the engine. `IResponseTemplateEngine.Render` returns `TemplateRenderResult(Text, Warnings)`.
- **Request context:** path parameters — `OperationKeyMatcher.TryMatch` (the values of `{name}` segments); query and headers are passed by the endpoint into `InvokeMockEndpoint` (`QueryParameters`/`Headers`); the body — the same one that goes to validation and history (a body longer than `CallRecordSnapshot.MaxLength` isn't parsed → body placeholders produce a warning).
- **Status from the spec** (3.8): `OpenApiSpecificationParser` → `ParsedOperation.ExampleStatusCode` → `MockEndpoint.ExampleStatusCode` (migration `AddExampleStatusCodeAndCallRecordWarnings`). **Old specs must be re-imported**, otherwise they answer 200. For statuses without a body (204, 304) no body is written. The response Content-Type is `application/json`, as before.
- **History:** `ResponseSnapshot` holds the already-rendered response, `StatusCode` the actual status; unresolved placeholders go into a new `CallRecord.Warnings` field (a JSON array), returned in `CallRecordSummary.Warnings` and shown in the record's details on the Call History page.
- **UI:** on the HTTP operation card, under the example — a hint about placeholders; the Send (try-it) button goes through `/mock`, so it shows the already-rendered response and its status.
- **Tests:** unit — `ResponseTemplateEngineTests` (every placeholder, escaping inside and outside a string, unresolvable values, JSON path over a nested body and with several matches, `uuid`/`now`, a non-JSON template), the parser's statuses (`example-statuses-openapi.yaml`), `TryMatch`, rendering/status/204/warnings in `InvokeMockEndpointHandlerTests`; integration — `ProviderModeApiTests.ServedOperation_RendersItsTemplate_AtTheSpecStatus` (path/query/body/uuid, 201, a warning in the history).
- **Deliberately not in this phase** (as in the brief): switching response scenarios (`X-Mock-Scenario: not-found` → 404), templates in response headers (response headers from the spec aren't stored yet). A Send scenario's payload (`RunTestScenarioHandler`) is still sent without substitution — placeholders in it go out as-is.

### 4.7 Phase G — An exchange for Send (RabbitMQ) — ✅ implemented

A Send scenario used to publish only to the default exchange `""` with routing key = the channel address (straight into the queue of the same name), while Listen bound its queue to an exchange (`amq.topic`). So Listen didn't hear our own Send, and services whose queue was bound to a topic/direct exchange didn't get the message. Decisions — 3.11–3.12.

- **Model:** `TestScenario.ListenExchange` → a shared `Exchange` (migration `RenameTestScenarioListenExchangeToExchange`, data preserved). On the wire the field is also called `exchange` (it was `listenExchange`).
- **Validation** (`TestScenarioTargetResolver.ValidateKindSettings`, formerly `ValidateListenSettings`): the exchange is stored (trimmed) only for a RabbitMQ connection, in both modes; for NATS and HTTP it's dropped. An empty value = the mode's default (Send — `""`, Listen — `amq.topic`).
- **`IMessageSender.SendAsync(…, exchange, …)`**: `MessageSender` publishes to the exchange with routing key = the channel address. For a non-empty exchange it first calls `ExchangeDeclarePassiveAsync` — a publish to a non-existent exchange doesn't return an error by itself (the broker closes the channel later), so without the check Send would "successfully" lose the message; a missing exchange gives a readable error, as in `MessageListener`.
- **UI:** the "Exchange" field is shown for a RabbitMQ connection in both modes, with a hint per mode; a new scenario starts with `amq.topic`.
- **Tests:** unit — the exchange is kept on a RabbitMQ Send, dropped on HTTP, and reaches `IMessageSender`; integration — a round trip on real RabbitMQ (`Listen_RabbitMq_HearsASendScenarioOnTheSameExchange`: Listen is started, a Send on the same operation publishes to `amq.topic`, Listen receives and validates) and a non-existent exchange (`Send_RabbitMq_ToAMissingExchange_FailsReadably` — needs a real broker, so it's in the integration tests, not unit tests). The existing test of publishing to the default exchange was unchanged.

### 4.8 Phase H — CORS on the provider port — ✅ implemented

The provider port used to answer without CORS headers, and a browser frontend pointed at the mock didn't pass the preflight (`OPTIONS` got a 404 from the mock). Decision — 3.13.

- **Configuration:** `Provider:CorsOrigins` (env `Provider__CorsOrigins`) — a comma-separated list, `*` means any origin; empty/unset — CORS is off, behaving as before. Parsing — `ProviderSettings.CorsOriginsFrom` (trimmed, no trailing `/`, no duplicates; `*` supersedes the rest).
- **Implementation:** `ProviderPortSetup` registers a separate `Provider` policy (any methods and headers, no credentials) only when origins are set, and in the `MapProviderPort` branch (`MapWhen`) calls `UseCors` before `Run`. The CORS middleware answers the preflight itself (204) — it never reaches the mock and isn't written to the history; to normal mock responses it adds `Access-Control-Allow-Origin` for an allowed origin.
- **AppHost/Docker:** off by default; `AppHost.cs` has a commented-out example, the `Dockerfile` a comment about `-e Provider__CorsOrigins=…`.
- **UI:** `GET /api/system/provider` returns `corsOrigins`; a hint on the operation card says which origins a browser may call the provider port from, or that CORS is off.
- **Tests:** unit — configuration parsing (`ProviderSettingsTests`); integration — the fixture sets `Provider__CorsOrigins` only for the test graph (`AppHostFixture.AllowedProviderOrigin`), a preflight with an allowed origin gets `Access-Control-Allow-*`, with a disallowed one — 204 without them; a normal request stays the mock's response. The "no configuration" case is covered by the parsing unit test (an empty list → the policy isn't registered) — a second AppHost isn't started for it.
- **Not in this phase:** CORS for `/mock` on the main port — it has the same origin as the Admin UI, and mixing policies isn't worth it; if needed, as a separate task.

## 5. Implementation order (recommendation)

1. ✅ **Phase A** (schemas + validator) — done, see 4.1.
2. ✅ **Phase B** (Type 1) — done, see 4.2 (done before E at the customer's decision; the `CallRecord` fields from 3.7 were added in its migration).
3. ✅ **Phase E** (history) — done, see 4.5.
4. ✅ **Phase C** (Type 4) — done, see 4.3.
5. ✅ **Phase D** (Type 3) — done, see 4.4.
6. ✅ **Phase F** (response dynamics) — done, see 4.6 (done before G–H at the customer's decision).
7. ✅ **Phase G** (an exchange for Send) — done, see 4.7.
8. ✅ **Phase H** (CORS on the provider port) — done, see 4.8.

## 6. Deliberately out of this plan

- Switching mock response scenarios (`X-Mock-Scenario: not-found` → 404) — explicitly deferred to the next iteration in `project-brief.md`; placeholder templating was done in Phase F (4.6).
- Matching mock requests by query/body (currently only method + path) — a separate gap in the MVP scope, not required for Type 3 (validating a body ≠ matching on a body), but worth keeping in mind if two `MockEndpoint`s with the same method + path are ever needed.
- CI/CD integration of contract tests — explicitly outside the MVP (`project-brief.md`, "Deliberately out of the MVP").
