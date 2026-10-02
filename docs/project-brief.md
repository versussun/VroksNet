# VroksNet — Project Brief

**Status:** Draft v0.9 (synced with the code on 2026-10-02)
**Stack:** .NET 10, .NET Aspire, Clean Architecture, Blazor WebAssembly, Docker
**Users:** single internal team, no auth on MVP

An internal server for API mocking and contract testing against OpenAPI and AsyncAPI specifications — a Microcks equivalent on the .NET stack.

> Code structure and style rules live in `.claude/CLAUDE.md`. This document is about the product: why, what's in the MVP, architecture and plan.

## 1. Goal

The team needs a tool for two related jobs: (1) API mocking, so the frontend and neighboring services can be built and tested independently of whether the real backend is ready; and (2) contract testing, to check that real services actually conform to their declared specifications.

Unlike Microcks, the scope is deliberately narrow: no multi-tenancy, no built-in authentication, no CI/CD integration in the first stage. The service is deployed through .NET Aspire (in development, and as a container orchestrator) and as a single Docker setup in production inside the team's infrastructure.

## 2. MVP scope

Both specification kinds at once, with no "REST first, async later" split.

| Area | What it does |
|---|---|
| Specification import | Import OpenAPI (Swagger 2.0 and OpenAPI 3.0/3.1) and AsyncAPI **3.0** files, YAML or JSON, through the UI and the REST API. An AsyncAPI 2.x file imports without an error but with **zero operations** — the parser reads only the 3.0 layout (top-level `operations`) |
| REST mocks | Answer with the operation's example from the spec, templated (see "Response dynamics"), at the status the example came from. Matched on **method and path only** (path parameters included); query and body aren't used for matching. An operation without an example answers `{}` — there's no schema-based generation |
| Async mocks | **Publishers**: publish an AsyncAPI operation's example message to NATS, RabbitMQ or Kafka every 1s–24h, or on demand ("Publish now") |
| Mock management | A Blazor panel: imported specifications, per-operation enable/disable and "Serve at real path" (provider mode), a try-it call per HTTP operation. Examples can't be edited in the UI — change the spec and re-import it |
| Call history | Every incoming mock call (matched or not), every test-scenario run (HTTP request, broker publish, received broker message) and every Publisher publish, with request/response bodies (capped at 64K) and the contract-check result; filterable, with "Clear history" |
| Contract testing | Four kinds of tests (`docs/contract-testing-plan.md`): call a service and validate its response, publish to a broker, check incoming requests to the mock against the spec (provider mode), listen on a broker and validate the message. Run manually from the UI or the REST API; not wired into a pipeline |

### Response dynamics

Decided: simple **templating**, with no scripting and no scenario-based switching of responses.

Placeholders in the response **body** are filled in with values from the request at match time (response headers aren't templated — the spec's response headers aren't stored):

| Placeholder | Source |
|---|---|
| `{{request.path.<name>}}` | A path parameter's value from the URL |
| `{{request.query.<name>}}` | A query parameter's value |
| `{{request.header.<name>}}` | A request header |
| `{{request.body}}` / `{{request.body.<jsonpath>}}` | The whole request body, or a value from it (JSON path, `$.a.b` or `a.b`) |
| `{{uuid}}` | A generated GUID — for fields like `id` in the response |
| `{{now}}` | The current timestamp (ISO 8601) |

It works on top of the specification's examples: the engine takes the example as a template and fills in the placeholders before sending the response. Without placeholders, it's just the static example, as before. A placeholder that can't be filled in becomes `null` (or an empty string inside a quoted value) and is recorded as a warning in the call history. Publishers fill in `{{uuid}}` and `{{now}}` in their messages; Test Scenario payloads are sent as-is. The exact rules are in `docs/contract-testing-plan.md` 3.8–3.10.

**Deliberately not now:** switching a mock to a specific scenario (for example, a `X-Mock-Scenario: not-found` header → 404 instead of the happy path). That's a separate feature for a later iteration; at the start, the happy path with static/templated examples from the spec is enough.

### Specification versioning

Decided: re-importing a specification **always replaces** the previous version. There's no version history; only the latest import is active.

Whether an import "updates an existing spec rather than adding a new one" is decided by the **name/title inside the spec itself** (`info.title` in OpenAPI and AsyncAPI), not by the file name: a file can be renamed, while `info.title` is the service's meaningful identifier — the same approach Microcks takes.

Re-importing is an **idempotent update in place** (✅ implemented): operations are matched by key (`METHOD /path`, or `channel:action` for AsyncAPI; repeated keys are paired by their order in the spec).
- An operation still in the spec keeps its id, its enabled/disabled state and "Serve at real path"; only its example, status and schemas are refreshed. So Test Scenarios, Publishers and call records that reference it keep working.
- A new operation is added; an operation no longer in the spec is removed, and anything referencing it fails with "…no longer exists".
- Importing the same file again changes nothing.
- A renamed operation (a changed path or channel) can't be told apart from a removed one plus a new one.

Examples can't be edited in the UI, so there are no manual edits to lose.

### Deliberately out of the MVP

- Authentication, roles, multi-tenancy
- CI/CD integration (CLI, Maven/Gradle/NuGet plugins, webhook triggers)
- Security testing of specifications
- MQTT, WebSocket — can be added later if needed (Kafka was added after the MVP — see "Phase 05")
- Clustering / horizontal scaling
- Error scenarios (`X-Mock-Scenario` and the like) — see "Response dynamics" above
- Specification version history — see "Specification versioning" above

## 3. Architecture

The solution is already set up with Clean Architecture and orchestrated by .NET Aspire. The brief's product components map one-to-one onto the existing projects:

| Brief component | Solution project | Role |
|---|---|---|
| Orchestration (dev) | `VroksNet.AppHost` | Aspire AppHost: in development, runs ApiService and Web as separate processes with hot reload, plus NATS, RabbitMQ and Kafka as resources — no hand-written docker-compose |
| Cross-cutting infrastructure | `VroksNet.ServiceDefaults` | Telemetry, health checks (`/health`, `/alive`), resilience — used by ApiService (Web, as a browser app, doesn't reference it) |
| Mock API + Admin UI host | `VroksNet.ApiService` | Composition root: the REST API, mocks under `/mock/…` and, on a second port (7353), at the spec's real paths (provider mode); registers Mediator. In Production it also serves `VroksNet.Web`'s built static files (wwwroot + SPA fallback to `index.html`) — the only process in the container |
| Admin UI | `VroksNet.Web` | Blazor **WebAssembly** (standalone, a browser project). Pages: Home, Specifications (list and details), Test Scenarios, Publishers, Call History, Settings (storage, connections). In Production it isn't run as its own process — it's only built, and `VroksNet.ApiService` serves it |
| Use cases | `VroksNet.Application` | Importing specs, matching mock requests, running test scenarios, Publishers, call history — as use cases through [martinothamar/Mediator](https://github.com/martinothamar/Mediator); defines the interfaces (parsers, repositories, broker clients, schema validator, template engine) that Infrastructure implements |
| Domain | `VroksNet.Domain` | Specifications, mock endpoints, connections, test scenarios, Publishers and call records as entities, plus the rules on them (operation/connection compatibility, overlap of provider-mode operations, the Publisher schedule) — with no framework references |
| Infrastructure | `VroksNet.Infrastructure` | The OpenAPI and AsyncAPI parsers; broker and HTTP clients for connection tests, sends and listens; `PublisherBackgroundService`; the template engine and the JSON Schema validator; EF Core persistence with the serialized write queue |

Dependencies point inward, as described in `.claude/CLAUDE.md`: `Domain ← Application ← Infrastructure`, `Presentation → Application`.

### Deployment: one process, one image

**Starting point (historical note):** the project began from the standard Aspire Starter template, where `VroksNet.Web` was Blazor **Server** — its own Kestrel process and a separate image when published. That setup has no automatic "merge into one image on `ASPNETCORE_ENVIRONMENT=Production`".

**Decision (✅ implemented):** move `VroksNet.Web` to Blazor **WebAssembly** (standalone) and serve it as static files from `VroksNet.ApiService` in Production — the classic "backend serves the SPA" pattern.

What this means technically:
- `VroksNet.Web.csproj` switches its SDK to `Microsoft.NET.Sdk.BlazorWebAssembly` (a standalone client, with no Kestrel or server-side rendering of its own).
- In Production, `VroksNet.ApiService` serves the built wasm files as static files and falls back to `index.html` for unknown routes (`app.UseStaticFiles()` / `app.MapFallbackToFile("index.html")`; `UseBlazorFrameworkFiles()` isn't needed in .NET 8+).
- Build: a multi-stage Dockerfile — first `dotnet publish` for `VroksNet.Web`, its `wwwroot`/wasm output is copied into `VroksNet.ApiService`'s `wwwroot`, then `VroksNet.ApiService` itself is published. One Dockerfile, one final image, one process in the container.
- In development, `VroksNet.Web` and `VroksNet.ApiService` can still run as two processes through `AppHost` (this gives hot reload and a comfortable dev experience) — CORS has to be set up on `ApiService` so the WASM client on its own dev port can reach the API. That's fine: a dev/prod topology difference is expected here and doesn't contradict the "one image" goal, which is about what gets deployed and run in production.
- `.claude/CLAUDE.md` is updated for this setup (✅): `VroksNet.Web` is a WASM client, not a separate host; `VroksNet.ApiService` is the only server, serving both the API and the UI's static files.

### Storage: decision

The choice is made: **SQLite**. Zero external dependencies, one file inside the container (or on a mounted volume, so data survives container recreation), no separate Aspire resource for a database — it fits the single-image idea from the section above.

How it's set up (✅ implemented): without `ConnectionStrings:VroksNetDb` the app uses a shared in-memory database (the default for `dotnet run` and the AppHost — data lives until the process stops); with it set to a file path, a SQLite file. The Docker image sets `Data Source=/app/data/vroksnet.db` on a `/app/data` volume.

The previously noted concurrent-write risk (the async worker and the Mock API write to the database in parallel — call history, statuses) still stands and calls for several mandatory measures in Phase 00, not just "EF Core + SQLite with defaults":

- Enable **WAL journal mode** (`PRAGMA journal_mode=WAL`) — allows reads during writes and noticeably reduces `SQLITE_BUSY`.
- Set a reasonable `busy_timeout` (e.g. 5 seconds) on the connection, so concurrent writers wait for each other instead of failing immediately.
- **Serialize writes through a single channel** (✅ implemented as `IDbWriteQueue` + `DbWriteBackgroundService`: one worker alone writes to the database, while the Mock API and the background workers only enqueue) — removes races at the application level instead of relying on SQLite locks alone.
- Migrate with EF Core migrations from the start, even for SQLite — that eases a later move to PostgreSQL if the load grows beyond one team.

If concurrent writes still become a bottleneck, migrating to PostgreSQL remains possible through EF Core without rewriting the domain layer.

## 4. Tech stack

| Layer | Technology | Why |
|---|---|---|
| Platform | .NET 10 | The current LTS cycle, already chosen for the solution |
| Orchestration (dev) | .NET Aspire 13.6 (`AppHost`, `ServiceDefaults`) | Runs ApiService + Web + NATS + RabbitMQ + Kafka in dev with hot reload; not used in Production — a single image is deployed |
| Use-case dispatch | martinothamar/Mediator (source generator, no reflection) | Already adopted as the standard in `.claude/CLAUDE.md` — not MediatR |
| Backend / Mock API | ASP.NET Core (`VroksNet.ApiService`) | Composition root; in Production it also serves the Admin UI's static files |
| Admin UI | Blazor WebAssembly, standalone (`VroksNet.Web`) | A browser SPA client; in Production it's built and served as static files from `ApiService`, never run separately |
| OpenAPI parsing | Microsoft.OpenApi 3.x + Microsoft.OpenApi.YamlReader | The official .NET library; reads Swagger 2.0 and OpenAPI 3.0/3.1 |
| AsyncAPI parsing | A custom parser over SharpYaml | There's no typed AsyncAPI model on NuGet — the parser walks the raw YAML of the 3.0 layout and resolves the spec's own local `$ref`s. SharpYaml already comes with Microsoft.OpenApi.YamlReader, so no second YAML library is added |
| Contract checks and templating | JsonSchema.Net, JsonPath.Net | JSON Schema validation of bodies and messages; JSON path for `{{request.body.<jsonpath>}}` |
| Async brokers | NATS.Client.Core, RabbitMQ.Client, Confluent.Kafka | The official .NET clients for the chosen protocols |
| Storage | SQLite + EF Core (WAL mode, serialized writes) | See section 3 |
| Deployment | Docker, multi-stage build (Web → static files → ApiService) | One Dockerfile, one image, one process in production |

## 5. Roadmap

Six phases, each a working increment that can be shown to the team. All of them are done; what comes next is decided in the accepted ADRs (section 7).

### Phase 00 — Project skeleton ✅ done
A Clean Architecture + Aspire solution (`VroksNet.slnx`, all projects under `src/`, `.claude/CLAUDE.md` with the rules); SQLite + EF Core with WAL, busy timeout and writes serialized through a single channel (`DbWriteQueue`/`DbWriteBackgroundService`); NATS and RabbitMQ as Aspire resources in `AppHost`.
**Result:** `dotnet run --project VroksNet.AppHost` brings up the whole stack, infrastructure included

### Phase 01 — OpenAPI → REST mocks ✅ done
OpenAPI import with replace-by-title, dynamic routing under `/mock`, templated responses with the status from the spec (done as Phase F of `docs/contract-testing-plan.md`, 4.6). There's no schema-based response generation for operations without an example — they answer `{}`.

Loading and parsing OpenAPI (matched by `info.title`, replaced on re-import), answering with the spec's example with placeholder substitution (`{{request...}}`, `{{uuid}}`, `{{now}}`), dynamic request routing in `VroksNet.ApiService`.
**Result:** import a spec — get a working REST mock for its endpoints, with templated responses

### Phase 02 — Admin UI on Blazor WebAssembly ✅ done
A list of specifications, a mock card, enabling/disabling endpoints, a simple call-history view in `VroksNet.Web` (WASM); publishing as static files from `VroksNet.ApiService`, a multi-stage Dockerfile for the single image.
**Result:** mocks can be managed without calling the API directly, and the app deploys as one image

### Phase 03 — AsyncAPI → NATS / RabbitMQ ✅ done
AsyncAPI import; publishing and listening through test scenarios; **Publishers** — async mocks: a `Publisher` entity (operation + broker connection + exchange + interval from 1s to 24h), `PublisherBackgroundService` publishing whatever is due once a second, plus "Publish now" from the UI. The payload is a template (`{{uuid}}`, `{{now}}`), checked against the message schema, and every publish is written to the call history. The triggers are the schedule and a manual run; there's no "on an HTTP mock call" trigger (the customer's decision).

AsyncAPI parsing, a `BackgroundService` worker publishing messages in `VroksNet.Infrastructure`, configuring intervals/triggers from the UI.
**Result:** import an AsyncAPI spec — the service publishes mock events to a broker

### Phase 04 — Contract testing + polish ✅ done
**Done:** all four kinds of contract tests, call history, response dynamics, an exchange for Send and CORS on the provider port (Phases A–H of the plan), guides in `docs/guides/contract-testing/`, operations documentation in `docs/runbook.md`.

A manual "schema ⇄ real response" check, history and logs, operations documentation. The detailed implementation plan (the 4 kinds of tests, a review of what already exists, the order of phases) is in `docs/contract-testing-plan.md`.
**Result:** a tool that can be handed to the team without needing hand-holding

### Phase 05 — Kafka ✅ done
A third broker `Connection` type, `Kafka`: connection testing (a cluster metadata request), Send/Listen test scenarios and Publishers. The topic is the AsyncAPI channel address. The connection value is a bootstrap-servers list (`host:9092,host2:9092`) or librdkafka `key=value;…` settings (for SASL/TLS). Listen doesn't use a consumer group: the matching topics' partitions are assigned by hand from their current end, with no commits, so real consumers don't notice anything. In dev, the `kafka` Aspire resource.

## 6. First steps

- [x] Set up the git repository and the solution structure — already done (`VroksNet`, .NET 10 / Aspire, Clean Architecture)
- [x] Move `VroksNet.Web` to `Microsoft.NET.Sdk.BlazorWebAssembly` (standalone), set up CORS on `VroksNet.ApiService` for dev mode
- [x] Have `VroksNet.ApiService` serve the WASM build's static files (`UseStaticFiles`, `MapFallbackToFile("index.html")`) in Production
- [x] Write the multi-stage Dockerfile: publish `VroksNet.Web` → copy wwwroot into `VroksNet.ApiService` → publish `VroksNet.ApiService`
- [x] Update `.claude/CLAUDE.md` for the new setup (Web is a WASM client, not a separate host)
- [x] Wire up SQLite + EF Core: enable WAL mode and busy timeout, design serialized writes through a single channel/worker
- [x] Add NATS and RabbitMQ as resources in `VroksNet.AppHost` (via `AddNats`/`AddRabbitMQ`)
- [ ] Collect 2–3 real OpenAPI specs and 1–2 AsyncAPI specs from your services as test data — for now there are only the sample specs in `docs/samples/` (OpenAPI 3.0/3.1, Swagger 2.0, AsyncAPI 3.0 for Kafka/NATS/RabbitMQ — see `docs/samples/README.md`)
- [x] In `VroksNet.Domain`, add the `ApiSpecification` (with `Title` as the version-matching key), `MockEndpoint` and `CallRecord` entities; in `VroksNet.Application`, the first use case `ImportOpenApiSpec` through Mediator, implementing replace-by-title
- [x] Prototype OpenAPI parsing with Microsoft.OpenApi in `ImportOpenApiSpecHandler` — load a file and print the list of endpoints to the console/log
- [x] Design the placeholder substitution engine (`{{request.path.*}}`, `{{request.query.*}}`, `{{request.header.*}}`, `{{request.body.*}}`, `{{uuid}}`, `{{now}}`) on top of the spec's examples — `ResponseTemplateEngine`, Phase F (`docs/contract-testing-plan.md`, 4.6)

## 7. Open questions

None from the previous version — both are resolved (see "Response dynamics" and "Specification versioning" in section 2).

Next steps are decided in two accepted ADRs. Their questions were answered on 2026-10-03; the only one still open is where the Aspire NuGet package is published (ADR 0001).

- **VroksNet as an Aspire resource, provisioned at startup** (specs, connections and Publishers from files; the Aspire package in a separate repository, built on `docs/container-contract.md`) — `docs/adr/0001-aspire-integration-and-provisioning.md`, accepted.
- **Background, delayed and scheduled test runs** (run history, scheduling, CI suites) — `docs/adr/0002-background-and-scheduled-test-runs.md`, accepted.
