# VroksNet — Project Rules

> **Project brief:** product goal, MVP scope, architecture, roadmap and open questions live in `docs/project-brief.md` — read it before making product-scope decisions; this file governs code structure/style only.

.NET 10 / .NET Aspire solution. These rules govern how code is structured and written in this repo. Follow them by default; call out explicitly when a change would violate one.

## Solution layout

- `src/VroksNet.AppHost` — Aspire orchestration only (no business logic).
- `src/VroksNet.ServiceDefaults` — shared cross-cutting Aspire wiring (telemetry, health checks, resilience).
- `src/VroksNet.Domain` — Clean Architecture Domain layer. No project references.
- `src/VroksNet.Application` — Clean Architecture Application layer. References `VroksNet.Domain`; carries the Mediator package references (see below).
- `src/VroksNet.Infrastructure` — Clean Architecture Infrastructure layer. References `VroksNet.Application`.
- `src/VroksNet.ApiService` — the only server process / composition root. References `VroksNet.Application` and `VroksNet.Infrastructure`. In Production it also serves `VroksNet.Web`'s built WebAssembly output as static files (`UseStaticFiles` + `MapFallbackToFile("index.html")` — **not** `MapStaticAssets()`/`UseBlazorFrameworkFiles()`, see the Dockerfile note below) — one process, one Docker image, built via the root `Dockerfile` (multi-stage: publishes `VroksNet.Web` first, copies its `wwwroot` into the final image alongside `VroksNet.ApiService`'s own publish output).
- `src/VroksNet.Web` — Blazor **WebAssembly** (standalone, `Microsoft.NET.Sdk.BlazorWebAssembly`) Admin UI. Calls the API over HTTP (typed `HttpClient`, e.g. `WeatherApiClient`) — it is never a composition root, has no `Application`/`Infrastructure` references, and can't reference server-side project types anyway (WASM). Its `HttpClient.BaseAddress` comes from `wwwroot/appsettings.{Environment}.json` (`ApiService:BaseAddress`) — not Aspire's `https+http://` service-discovery scheme, which only resolves server-side and does nothing in browser-executed code. In dev it runs as its own process via `AppHost` for hot reload, hitting `ApiService`'s fixed dev URL with CORS enabled on `ApiService` for it; in Production it's build output only, served by `VroksNet.ApiService` (same origin, no CORS needed there).
- `tests/VroksNet.IntegrationTests` — Aspire integration tests (boots the real `AppHost` graph). See "Testing" below.
- `tests/VroksNet.E2ETests` — Playwright browser E2E tests, also booting the real `AppHost` graph. See "Testing" below.
- `tests/VroksNet.UnitTests` — handler/parser/repository unit tests. See "Testing" below.

Only `VroksNet.ApiService` is a composition root, in both dev and prod. `VroksNet.Web`'s Razor components talk to the backend only over HTTP (typed `HttpClient` calling `VroksNet.ApiService`'s endpoints) — they never reach into `Application`/`Infrastructure`/`Domain` types directly, since a WASM client can't reference server-side project types anyway.

## Architecture: Clean Architecture

Dependencies point inward only. Outer layers depend on inner layers; inner layers know nothing about outer ones.

```
Domain  ←  Application  ←  Infrastructure
                ↑
          Presentation (ApiService / Web)
```

- **Domain** — entities, value objects, domain events, domain exceptions, enums. No framework or infrastructure references (no EF Core, no ASP.NET, no HTTP clients). This is the innermost layer and has no project dependencies of its own.
- **Application** — use cases, orchestration logic, DTOs, validation, and the Mediator requests/handlers (see below). Depends only on Domain. Defines interfaces (repositories, external services) that Infrastructure implements — dependency inversion, no concrete infrastructure types here.
- **Infrastructure** — implementations of Application's interfaces: persistence (EF Core, repositories), external service clients, file system, email, etc. Depends on Application (and transitively Domain).
- **Presentation** (`VroksNet.ApiService`, `VroksNet.Web`) — thin. Controllers/endpoints/components map HTTP or UI concerns to Mediator requests and back to responses. No business logic here; delegate to Application via the mediator. Depends on Application and Infrastructure only to wire up DI at startup (composition root) — request-handling code should only ever reach into Application.
- Never reference outward: Domain must not reference Application/Infrastructure/Presentation; Application must not reference Infrastructure or Presentation.

## Mediator: martinothamar/Mediator

Use [martinothamar/Mediator](https://github.com/martinothamar/Mediator) (source-generator based, no reflection) for all use-case dispatch — not MediatR.

**Packages** — already added to `VroksNet.Application` and pinned centrally in `Directory.Packages.props` (Central Package Management rejects floating versions, so bump this exact pin deliberately rather than using a wildcard):

```xml
<PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
<PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
```

**Registration — `AddMediator(...)` must be called from `VroksNet.Application` itself, not from `VroksNet.ApiService`.** The Mediator source generator only inspects `AddMediator` calls made within its own project's compilation (the one referencing `Mediator.SourceGenerator`, i.e. Application) — a call from a downstream project like ApiService is invisible to it, so the generator bakes in the default (Singleton) lifetime regardless of what's requested there, and the app throws at startup (`Invalid configuration detected for Mediator... generated code for 'Singleton' lifetime, but got 'Scoped'`) the moment the two disagree. This bit us once already — don't reintroduce it.

The fix already in place: Application exposes its own DI extension, and `Program.cs` just calls that:

```csharp
// VroksNet.Application/ApplicationServiceCollectionExtensions.cs
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddMediator(options =>
    {
        options.ServiceLifetime = ServiceLifetime.Scoped; // matches VroksNet.Infrastructure's scoped DbContext usage
    });
    return services;
}
```
```csharp
// VroksNet.ApiService/Program.cs
builder.Services.AddApplication();
```

If `VroksNet.Web` ever gets its own composition root, it still can't call `AddMediator` directly for the same reason — go through `AddApplication()`.

**Requests and handlers** live in Application, grouped by feature (e.g. `Application/Orders/CreateOrder/`), one request + handler pair per file-group:

```csharp
public sealed record CreateOrder(string CustomerId, decimal Amount) : IRequest<OrderId>;

public sealed class CreateOrderHandler(IOrderRepository repository) : IRequestHandler<CreateOrder, OrderId>
{
    public async ValueTask<OrderId> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        // orchestrate domain logic, call repository
    }
}
```

- Requests/notifications are `sealed record`s implementing `IRequest<TResponse>`, `IRequest`, or `INotification`.
- Handlers are `sealed class`es implementing `IRequestHandler<,>` / `IRequestHandler<>` / `INotificationHandler<>`.
- Cross-cutting concerns (validation, logging, transactions) go in `IPipelineBehavior<,>` implementations registered via `options.PipelineBehaviors = [...]`, not scattered across handlers.
- Presentation calls only `IMediator.Send(...)` / `IMediator.Publish(...)`; it never calls a handler directly.

## Infrastructure notes

- **Persistence: SQLite via EF Core, one `DbContext`, all writes serialized.** `VroksNet.Infrastructure.Persistence.VroksNetDbContext`, resolved through `IDbContextFactory<VroksNetDbContext>` (not injected directly) so every read/write gets its own short-lived context. Writes never go straight to the context — call sites (e.g. `ApiSpecificationRepository`) enqueue a delegate through `IDbWriteQueue`, and the single `DbWriteBackgroundService` consumer executes them one at a time. This is deliberate (see `docs/project-brief.md` section 3 on the concurrent-write risk between the Mock API and the async publish worker) — don't add a second write path that bypasses the queue. WAL journal mode and a 5s busy_timeout are applied in `InfrastructureServiceCollectionExtensions` — `AddInfrastructure(...)` for the connection setup, `InitializeDatabaseAsync()` (called once at ApiService startup) for migrations + the `PRAGMA journal_mode=WAL` statement.
- **Spec replace semantics**: `ApiSpecificationRepository.UpsertAsync` deletes any existing row with the same `Title` (cascades to its `MockEndpoint`s) and inserts fresh, rather than attaching/patching a detached graph — that path threw `DbUpdateConcurrencyException` when tried. `Title` has a unique index (`VroksNetDbContext.OnModelCreating`); don't relax it without revisiting this.
- **Migrations**: run from the repo root — `dotnet ef migrations add <Name> --project src/VroksNet.Infrastructure --startup-project src/VroksNet.ApiService --output-dir Persistence/Migrations`. `Microsoft.EntityFrameworkCore.Design` has to be referenced by **both** projects (Infrastructure for the model, ApiService because `dotnet ef` requires it on whatever `--startup-project` is) — the tools give a clear error if either is missing.
- **OpenAPI parsing**: `Microsoft.OpenApi` + `Microsoft.OpenApi.YamlReader` (3.x line). The YAML reader isn't auto-registered — call `new OpenApiReaderSettings().AddYamlReader()` (extension in `Microsoft.OpenApi.Reader`) and pass those settings into `OpenApiDocument.LoadAsync(stream, "yaml", settings, cancellationToken)`, or you'll hit `NotSupportedException: Format 'yaml' is not supported.`
- **AsyncAPI parsing** (`AsyncApiSpecificationParser`): there's no AsyncAPI counterpart to `Microsoft.OpenApi`'s typed object model on NuGet, so it walks the raw YAML tree via `SharpYaml`'s representation model (`YamlMappingNode`/`YamlSequenceNode`/`YamlScalarNode`) and resolves the spec's own local `"#/a/b/c"` `$ref`s by hand — deliberately minimal, this spec's own refs only, not a general JSON Reference/external-file implementation. `SharpYaml` is referenced directly in `VroksNet.Infrastructure.csproj` rather than added as a second general-purpose YAML library: it's already pulled in transitively by `Microsoft.OpenApi.YamlReader` (pin it to the exact version that package brings — `2.1.5` as of writing — so it can't silently drift out of sync with that transitive edge). Operation keys follow `MockEndpoint.OperationKey`'s documented AsyncAPI convention, `"{channel address}:{action}"` (e.g. `"orders.created:send"`) — these are **not** invokable through `/mock/{**path}` (`MockInvocationEndpoints`/`OperationKeyMatcher` assume HTTP method+path semantics); actually publishing them is the Phase 03 async-mock publish worker, not built yet (see docs/project-brief.md section 2, and the "Message brokers" bullet above).
- **Two parser interfaces, not one shared one.** `ISpecificationParser` (→ `OpenApiSpecificationParser`) and `IAsyncApiSpecificationParser : ISpecificationParser` (→ `AsyncApiSpecificationParser`) are registered side by side in `AddInfrastructure`. `IAsyncApiSpecificationParser` adds no members of its own — it exists purely so DI can tell the two registrations apart; each kind's import handler (`ImportOpenApiSpecHandler` / `ImportAsyncApiSpecHandler`) asks for its own kind by type instead of both racing for the same unqualified `ISpecificationParser` registration (last registration would silently win). The two kinds stay fully separate vertical slices end to end — own `Application/Specifications/Import{Kind}Spec` request+handler, own `POST /api/specifications/{openapi|asyncapi}` endpoint — rather than one handler branching on a `Kind` parameter; follow that pattern if a third spec kind shows up.
- **`Microsoft.AspNetCore.OpenApi` is deliberately not referenced anywhere.** ASP.NET Core's own OpenAPI self-documentation package hard-pins `Microsoft.OpenApi < 3.0.0` even at its latest version, which conflicts with the `Microsoft.OpenApi` 3.x line the spec-parsing feature needs (`Microsoft.OpenApi.YamlReader` only exists for 3.x). If ApiService's own `/openapi/v1.json` self-doc is wanted later, it needs a different mechanism (e.g. Scalar, or hand-rolled) that doesn't drag in the 2.x `Microsoft.OpenApi` — don't just re-add the package, it'll bring back a `NU1107` version conflict.
- **Message brokers**: both RabbitMQ and NATS are in MVP scope (`docs/project-brief.md` section 2) and already wired as Aspire-managed resources (`AppHost.cs`: `AddRabbitMQ("rabbitmq")`, `AddNats("nats")`) with matching client packages in ApiService (`Aspire.RabbitMQ.Client` → `AddRabbitMQClient("rabbitmq")`, `Aspire.NATS.Net` → `AddNatsClient("nats")`). Only connectivity is proven so far — the actual async-mock publish worker (`BackgroundService` publishing on AsyncAPI channels) is Phase 03 work, not built yet.
- **Connection testing** (`IConnectionTester` → `ConnectionTester`, `POST /api/connections/{id}/test`): actually reaches a stored `Connection`'s target rather than just validating shape. This is deliberately **not** the same client path `AddRabbitMQClient("rabbitmq")`/`AddNatsClient("nats")` use above — those are Aspire-wired to the dev-time broker resources in `AppHost.cs`; a `Connection` holds an arbitrary user-typed host/credentials that has nothing to do with those, so `ConnectionTester` opens its own client directly: a short-timeout `HttpClient` GET for `Http` (any HTTP response counts as reachable, even a 4xx — only a thrown exception counts as failure), `RabbitMQ.Client`'s `ConnectionFactory.CreateConnectionAsync` for `RabbitMq`, and `NATS.Client.Core`'s `NatsConnection.PingAsync` for `Nats` — both referenced directly in `VroksNet.Infrastructure.csproj` (not the `NATS.Net`/`Aspire.NATS.Net` meta-packages, which pull in JetStream/KV/ObjectStore/Hosting nothing here needs) and pinned to the exact versions those two Aspire client packages themselves already resolve, so the direct reference can't drift out of sync with what's already in the graph. All three branches share one 5s timeout and return `ConnectionTestResult(bool Success, string Message)` — `Message` is written to be safe to show verbatim in the UI (never a raw connection string or full stack trace). `tests/VroksNet.UnitTests/Connections/ConnectionTesterTests.cs` exercises the *real* class (not a fake) against definitely-closed local ports and a `.invalid` (RFC 2606) hostname — fast and deterministic, no Docker needed, unlike the success-path coverage in `VroksNet.IntegrationTests` (which points an `Http` connection back at the booted ApiService's own `/health`).
- **Dev-time ports under AppHost are random, confirmed by direct testing (not just assumed).** Running `dotnet run --project src/VroksNet.AppHost` — with or without an explicit launch profile — assigns `apiservice` and `webfrontend` a fresh random port every run; each project's own `launchSettings.json` applicationUrl is **not** honored when launched this way. This broke Web's CORS calls to ApiService until diagnosed by directly inspecting listening ports (`Get-NetTCPConnection`) rather than trusting the hardcoded launchSettings values.
  - **Fix in place**: `apiservice`'s endpoint is pinned in `AppHost.cs` — `.WithHttpsEndpoint(port: 7352, name: "https")` — matching the port hardcoded in `VroksNet.Web/wwwroot/appsettings.Development.json` (`ApiService:BaseAddress`). This works via Aspire's DCP proxy (`dcp.exe` binds the stable port and forwards to ApiService's actual random internal port) — don't be alarmed that ApiService's own process shows a *different* listening port than 7352 when inspected directly; 7352 is the one that matters, and it's proxied correctly. If `apiservice`'s pinned port ever changes, update both places together.
  - `webfrontend`'s own dev-server port is deliberately left unpinned — ApiService's Development CORS policy allows any loopback origin (`SetIsOriginAllowed`) instead of a fixed origin list, specifically so Web's random port doesn't matter.
  - **`Uri.IsLoopback` alone isn't enough.** Aspire's newer per-resource dev-domain hostnames (e.g. `https://webfrontend-vroksnet.dev.localhost:7043` — `<resource>-<appname>.dev.localhost`) are real origins Web's browser tab can end up on, but `Uri.IsLoopback` only matches the literal host `localhost` or a loopback IP literal — it returns `false` for any `*.localhost` subdomain, confirmed directly (`[Uri]::new("https://foo.localhost").IsLoopback` → `False`) even though the whole `.localhost` TLD is reserved by RFC 6761 to always resolve to loopback. The CORS predicate checks `IsLoopback || Host.EndsWith(".localhost")` for exactly this reason — don't narrow it back to just `IsLoopback`. For anything outside both patterns (a real custom hostname via the hosts file, a tunnel domain), add it via the `Cors:AdditionalDevOrigins` config key (env var: `Cors__AdditionalDevOrigins=https://foo.example,https://bar.example`) rather than widening the CORS policy itself.

## Testing

- `tests/VroksNet.IntegrationTests` — Aspire integration tests: boots the *real* distributed-application graph (`AppHost.cs` as-is — `apiservice`, `webfrontend`, and the RabbitMQ/NATS containers `apiservice` waits on) via `Aspire.Hosting.Testing`, then drives it over the wire. It doesn't reference Domain/Application/Infrastructure directly — tests talk JSON/HTTP against the real endpoints (black-box), not server-side types, so they can't silently pass against an internal shape that doesn't match the actual wire contract.
  - **One shared boot per test run, not per test.** Booting the graph starts real containers and takes several seconds; `Fixtures/AppHostFixture.cs` (`IAsyncLifetime`) builds and starts it once, and `Fixtures/AppHostCollection.cs` (`[CollectionDefinition("AppHost")]` + `ICollectionFixture<AppHostFixture>`) shares that one instance across every test class in the `[Collection("AppHost")]` group — which also makes them run sequentially against the same `apiservice` process and its single-writer SQLite queue (see "Infrastructure notes"), so tests must not assume isolated state between each other.
  - **Requires a running container runtime** (Docker Desktop or compatible) — `AppHost.cs`'s `apiservice` resource `.WaitFor(rabbitmq).WaitFor(nats)`, so these tests can't boot without one. Skip/don't run this project in an environment with no container runtime available.
  - **The backing SQLite file persists across runs** (same `vroksnet.db` a normal dev run would use, unless overridden) — tests must not assume a clean database. Use GUID-suffixed values for anything with a unique constraint (e.g. `Connection.Name`) so repeated runs don't collide; specification imports are naturally safe to repeat since `UpsertAsync` replaces by `Title` (see "Infrastructure notes").
- `tests/VroksNet.E2ETests` — Playwright browser E2E tests: boots the same real `AppHost` graph as `VroksNet.IntegrationTests` (same one-shared-boot-per-run, same-container-runtime, same-persisted-database caveats above all apply here too), then drives `webfrontend` through an actual Chromium instance rather than calling the API directly — this is the layer that would catch a Blazor component wired up wrong even though the API underneath is fine.
  - `Fixtures/AppHostFixture.cs` additionally launches **one shared Playwright `IBrowser`** in the same `InitializeAsync` (also too slow to redo per test) and exposes `WebBaseAddress` (`App.GetEndpoint("webfrontend")`) for tests to navigate against. Headless by default; run with `HEADED=1` locally to watch a test execute while debugging it.
  - `Fixtures/PageTestBase.cs` is the per-test-class base every test class extends: since xUnit constructs a fresh test-class instance per test method, its `IAsyncLifetime.InitializeAsync`/`DisposeAsync` opening/closing a fresh `IBrowserContext`/`IPage` there gives each test its own cookies/local-storage isolation for free, while still reusing the one shared browser process.
  - **Playwright's browser binaries must be installed once** before these tests can run — build the project, then run the generated `bin/Debug/net10.0/playwright.ps1 install` (add `--with-deps` on a fresh Linux CI box). Not needed again until the pinned `Microsoft.Playwright` version (`Directory.Packages.props`) changes.
  - Prefer `Page.GetByRole`/`GetByLabel`/`GetByText` locators over CSS selectors — they're what make a Razor component's markup need real `<label for>`/ARIA-role structure to be testable at all, which is worth fixing in the component if missing (see `Settings.razor`'s `for`/`id` pairs, added specifically so `GetByLabel` could find its inputs) rather than reaching for a brittle CSS locator instead.
- `tests/VroksNet.UnitTests` — everything else: handler tests (fakes for `IApiSpecificationRepository`/`ISpecificationParser` in `TestDoubles/`, no mocking library — plain hand-written stand-ins are enough for interfaces this small), parser tests against fixture YAML files under `Fixtures/` (`CopyToOutputDirectory`, read via `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)`), and repository tests that run against a real temp-file SQLite database through the actual `DbWriteQueue`/`DbWriteBackgroundService` (not a fake queue) — that's what caught the replace-by-title bug in the first place, so don't downgrade it to an in-memory fake.
- All four test projects: xUnit v3 idiom — pass `TestContext.Current.CancellationToken` to every cancellable call in a test body (analyzer-enforced, `xUnit1051`), not `CancellationToken.None`. Constructor/dispose lifecycle code (`IAsyncLifetime.InitializeAsync`/`DisposeAsync`) is the one exception — no test context exists yet at that point, so `CancellationToken.None` there is correct.
- A SQLite-backed test's `DisposeAsync` must call `SqliteConnection.ClearAllPools()` before deleting its temp `.db` file — `Microsoft.Data.Sqlite` pools the native connection by default, so the file can still be locked immediately after every `DbContext` using it has been disposed.

## .NET coding rules

- **Seal by default.** Every class is `sealed` unless it is explicitly designed as a base class for inheritance (i.e. it already has, or is deliberately meant to have, derived classes). If you're not designing for extension, seal it — this includes handlers, services, and DTOs.
- **Nullable reference types** enabled everywhere (already set per-project) — don't suppress warnings with `!` unless truly unavoidable; add a comment when you do.
- **File-scoped namespaces** (`namespace Foo.Bar;`) in all new files.
- **Prefer records** for immutable data (DTOs, Mediator requests/notifications, value objects). Prefer primary constructors for simple dependency injection into classes.
- **`async`/`await` all the way** — no `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` on async code; suffix async methods with `Async` outside of Mediator handlers (`Handle` keeps the interface's name).
- **One public type per file**, file name matches the type name.
- Favor expression-bodied members for trivial one-liners; keep everything else in a normal method body for readability.
- No business logic in Presentation projects — it belongs in Application (orchestration) or Domain (rules/invariants).
