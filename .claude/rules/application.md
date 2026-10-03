---
paths:
  - "src/VroksNet.Application/**"
---

# VroksNet.Application

The Clean Architecture Application layer: use cases, orchestration, DTOs, validation, and Mediator requests/handlers. It references only `VroksNet.Domain`.

## Rules

- **Depend only on Domain.** Define interfaces here (repositories, external services) for Infrastructure to implement. No concrete infrastructure types in this project.
- **Use [martinothamar/Mediator](https://github.com/martinothamar/Mediator) for all use-case dispatch**, not MediatR. It is source-generator based and uses no reflection.
- **Call `AddMediator(...)` only from inside Application**, via `AddApplication()`. Never call it from ApiService or any other project (see Gotchas).
- **Group requests + handlers by feature**, e.g. `Application/Orders/CreateOrder/`, with one request + handler pair per file group.
- **Requests and notifications are `sealed record`s** implementing `IRequest<TResponse>`, `IRequest` or `INotification`.
- **Handlers are `sealed class`es** implementing `IRequestHandler<,>` / `IRequestHandler<>` / `INotificationHandler<>`. `Handle` keeps the interface's name, with no `Async` suffix.
- **Put cross-cutting concerns (validation, logging, transactions) in `IPipelineBehavior<,>`** implementations registered via `options.PipelineBehaviors = [...]`. Don't scatter them across handlers.
- **Bump Mediator versions deliberately.** They are pinned exactly in `Directory.Packages.props` (Central Package Management rejects floating versions):

  ```xml
  <PackageVersion Include="Mediator.Abstractions" Version="3.0.2" />
  <PackageVersion Include="Mediator.SourceGenerator" Version="3.0.2" />
  ```

## Canonical shape

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

// VroksNet.ApiService/Program.cs
builder.Services.AddApplication();
```

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

## Gotchas

- **Why `AddMediator` must live in Application:** the source generator only inspects `AddMediator` calls inside its own compilation, which is the project referencing `Mediator.SourceGenerator`. A call from ApiService is invisible to it, so the generator bakes in the default Singleton lifetime, and startup then throws `Invalid configuration detected for Mediator... generated code for 'Singleton' lifetime, but got 'Scoped'`. This has happened before; don't reintroduce it. If Web ever gets its own composition root, it still has to go through `AddApplication()`.

## Feature notes

- **Test Scenarios** (`TestScenario` entity, `Application/TestScenarios/*`, `/api/test-scenarios`, the "Test Scenarios" Web page): a saved "send this message somewhere". It combines one operation (`MockEndpointId`) from one specification (`SpecificationId`), sent through one `Connection` (`ConnectionId`), with an optional `PayloadOverride`. Sending goes through `IMessageSender`/`MessageSender`, the sibling of `ConnectionTester`. Both dispatch to the same per-type broker adapter, which actually publishes/POSTs. See "Connections and broker adapters" in `.claude/rules/infrastructure.md`.
  - **Don't add FK constraints from `TestScenario`** to the specification, operation or connection it references. This is the same loose coupling as `CallRecord`. When a reference is deleted, the scenario fails gracefully at run time: `RunTestScenarioHandler` returns an unsuccessful result and doesn't throw. `ListTestScenariosHandler`'s `TestScenarioSummary` shows `"(deleted specification)"` / `"(deleted connection)"` placeholders instead of crashing.
  - **Every run logs a `CallRecord`** through `ICallRecordRepository`; the call history (`Application/CallRecords/*`, `/api/call-records`, the "Call History" page) reads them back. The record uses `CallDirection.OutboundHttpRequest` (an `Http`-connection run is neither `InboundHttpRequest` nor `OutboundBrokerPublish`) and sets `CallRecord.ConnectionId`, `TestScenarioId`, `StatusCode` and the contract outcome (`ContractValid`/`ValidationErrors`).
  - **`TestScenario.Kind`: `Send` or `Listen`.** `Send` is the HTTP request or broker publish; whether it's HTTP or broker follows from the connection type. `Listen` (AsyncAPI operations only) waits on the channel through `IMessageListener`, then validates the message against the payload schema (`MockEndpoint.ResponseSchema`). A new scenario's default mode comes from `TestScenarioListening.DefaultKindFor`: an AsyncAPI `send` operation defaults to Listen. Keep these rules in Domain. Web gets them through `MockEndpointDetail` (`RequiresHttpConnection`, `CanListen`, `DefaultTestScenarioKind`) and must not re-derive them from the operation key. Validation of the kind's settings lives in `TestScenarioTargetResolver.ValidateKindSettings`.
  - **`TestScenario.Exchange` is RabbitMQ-only and shared by both kinds** (contract-testing "Phase G"; it was `ListenExchange`). Send publishes to it with routing key = channel address, and null means the default exchange `""`. Listen binds to it, and null means `amq.topic`. A Send and a Listen on the same operation and exchange therefore meet. Existing scenarios keep null, so their behavior hasn't changed. The UI starts new ones at `amq.topic`. It's dropped for NATS/HTTP connections.
  - **An `Http` run's response is validated against the spec** (contract-testing "Phase B", see `docs/contract-testing-plan.md` 4.2). The schema is picked by the *actual* status code via `MockEndpoint.TryGetDeclaredResponse` (exact code, then `"2XX"`, then `"default"`), and an undeclared status is itself a violation. A violation fails the run: `Success`/`LastRunSuccess` mean "sent *and* matched the spec", and `RunTestScenarioResult.ContractValidation` says which part failed. It is null when nothing was checked, i.e. a broker publish, a failed send, or an operation with no stored responses (imported before Phase B, so re-import the spec).
- **Names are unique per kind** for test scenarios, Publishers and connections (step A2; provisioning in ADR 0001 refers to them by name).
  - Every create/update handler goes through `UniqueNames`: `Normalize` (required, stored trimmed) and `EnsureFree` (the holder found by `FindByNameAsync` must be the object itself). A taken name is an `ArgumentException`, a 400 at the endpoint.
  - A unique index backs each (`VroksNetDbContext`), so a race between two creates still can't produce duplicates; the loser gets a 500, which is acceptable for an admin action.
  - Comparison is exact (case-sensitive), like SQLite's default collation on the index.
- **Provisioning** (`Application/Provisioning/*`, ADR 0001): `ApplyProvisioning` applies specs, connections, spec settings, Publishers, test scenarios, then test suites (scenarios by name).
  - **Everything goes through the existing use cases over Mediator** (`ImportOpenApiSpec`, `CreateConnection`/`UpdateConnection`, `SetEndpointEnabled`, `SetSpecificationProviderMode`, `CreatePublisher`/`UpdatePublisher`, …), so provisioning validates exactly like the UI. Don't write to repositories directly from it, except `IProvisionedMarker` for `ProvisionedAt`.
  - **Objects are matched by name** (specs by title); applying the same input twice changes nothing. Removed manifest entries are left in place — provisioning never deletes.
  - **One broken entry doesn't stop the rest:** each failure becomes a `ProvisioningError` with its source (`specs/x.yaml`, `publishers[name]`), and the status is `Failed`. Whether that stops the app is Infrastructure's call.
  - **Export (A7)** is the reverse: `ExportProvisioningHandler` builds the manifest by name from the current state, and `IProvisioningPackageWriter` (Infrastructure) zips it with the specs' raw content. Whatever you add to the manifest, add to the export too. `ExportProvisioningTests` round-trips it (export → provision an empty instance → same objects), so a field that's applied but never exported fails there only if the test covers it; extend its manifest.
  - `ProvisioningState` (singleton) holds the latest `ProvisioningReport` for the health check and `GET /api/system/info` (`System/GetSystemInfo`, with `IAppVersionProvider` and `ContainerContract.Version` — keep the latter equal to the Dockerfile's `io.vroksnet.contract.version` label; `scripts/verify-container-contract.sh` fails CI when they differ).
- **Test runs** (`TestRun` entity, `Application/TestRuns/*`, `/api/test-runs`, `POST /api/test-scenarios/{id}/runs`; ADR 0002).
  - **One run logic: `TestScenarioExecutor`.** The synchronous `RunTestScenarioHandler` and the background `ExecuteTestRunHandler` both call it and only own the `TestRun` around it. Don't put run logic back into either handler. Every `CallRecord` it writes carries the `TestRunId`.
  - **Every run is a `TestRun`,** synchronous ones included (`Trigger = Manual`). A run must never stay `Running`: both handlers complete it on success, failure, cancellation and unexpected exceptions, writing with `CancellationToken.None`.
  - **State changes are conditional** in `ITestRunRepository` (Queued → Running, Running → final, Queued → Cancelled). A cancel racing a start, or a late completion, changes nothing and returns false; don't replace them with unconditional updates.
  - **Cancellation goes through `TestRunCancellations`** (a singleton, in memory: one process). Both handlers register the run's token there, so `CancelTestRun` can stop synchronous and background runs alike. It also tells a deliberate cancel (`Cancelled`) from shutdown (`Interrupted`).
  - **Suites (B4)** (`Domain/TestSuites`, `Application/TestSuites/*`, `/api/test-suites`, `/api/suite-runs`). `ExecuteSuiteRunHandler` is the one place the "Listens first, Sends once they're subscribed" rule lives. It runs every run through `TestRunRunner` (shared with `ExecuteTestRun`; don't copy the run lifecycle). The runner swallows a run's cancellation, so the suite checks its own token between steps. Suite runs reuse `TestRunStatus`/`TestRunTrigger` and `TestRunCancellations` (keyed by suite run id; a run cancelled with its suite is `Cancelled`, via `parentId`). `{suite}` keys resolve through `TestSuiteKeys` (a GUID → by id, otherwise by name).
  - **Startup suites (B5):** `TestSuite.RunOnStartup`; `StartStartupSuiteRuns` queues them (`Trigger = Startup`). Only `ProvisioningHostedService` sends it, after provisioning is `Applied` or `NotConfigured`.
  - **Delayed runs (B3)** are just a `StartTestRun` with `RunAt`/`DelaySeconds`: a `Queued` run with `Trigger = Delayed` and a future `ScheduledFor`, which the worker picks up like any other. `StartTestRunHandler` holds the limits (`MaxDelay`, 30 days). Unlike scheduled runs, `SkipMissedScheduledTestRuns` leaves them alone: a late delayed run still runs.
  - **Scheduling (B2):** `TestScenario.Schedule`/`ScheduleTimeZone` are plain strings; all cron arithmetic goes through `ICronSchedule` (Cronos in Infrastructure — never reference Cronos from Application/Domain). `TestScenarioSchedules.Normalize` is the one validation, used by create/update (and so provisioning). `QueueScheduledTestRunsHandler` holds the anchoring rule (next occurrence after the last scheduled run's `ScheduledFor`; if already past, the first from now) — keep it there. Changing a schedule or deleting a scenario deletes its queued scheduled run.
  - **Listen timeouts:** up to `TestScenarioListening.MaxTimeoutSeconds` (1800). Over `MaxSynchronousTimeoutSeconds` (80) a scenario only runs in the background; the synchronous run refuses it without running.
- **Publishers** (`Publisher` entity, `Application/Publishers/*`, `/api/publishers`, the "Publishers" Web page; `docs/project-brief.md` Phase 03): async mocks that publish one AsyncAPI operation's message through a RabbitMQ/NATS/Kafka `Connection` every `IntervalSeconds` (1s–24h) while enabled, or on demand (`PublishNow`).
  - They're separate from Test Scenarios on purpose: a scenario is a one-off check, a publisher is background work. Same loose coupling, though: no FKs, and a deleted reference makes the publish fail rather than throw.
  - `PublisherRules.ValidateAsync` (shared by create/update) rejects HTTP operations, out-of-range intervals and blank or taken names with `ArgumentException`; `PublisherEndpoints` maps that to a 400 with the reason.
  - `Exchange` works as in Test Scenario Send: RabbitMQ only, null = the default exchange.
  - `PublishNowHandler` renders the payload (`PayloadOverride ?? ExampleTemplate`) with an empty `TemplateContext`, so only `{{uuid}}`/`{{now}}` resolve and `{{request.*}}` becomes a warning. It validates the payload against `MockEndpoint.ResponseSchema` (reported, never failing the publish), logs a `CallRecord` with `PublisherId` (`OutboundBrokerPublish`) and records the outcome on the publisher.
- **Call History** (`Application/CallRecords/*`, `/api/call-records`, the "Call History" Web page): every Test Scenario run, every publish and every inbound mock call, newest first.
  - **Page with the keyset cursor, never offsets.** `ListCallRecords` returns `NextCursor`; new records arriving between requests must not shift pages.
  - **`InvokeMockEndpointHandler` logs every mock call, matched or not** (`InboundHttpRequest`, with `RequestLine`). It also decides the response body (the example, or `"{}"`), so the history holds the body that was served; for a 404 it holds the detail text, which the endpoint wraps in ProblemDetails. Don't move that choice back into the endpoint.
  - **Mock logging is best-effort.** A failed history write is logged as a warning and the mock still answers; never let it turn a matched mock call into a 500.
  - **Cap stored bodies with `CallRecordSnapshot.Truncate`** (64K chars plus a "…(truncated)" marker) wherever a `CallRecord` is written. The history list (`CallRecordSummary`) carries no bodies; `GetCallRecord` returns them for one record.
  - **Resolve display names through `ICallRecordNameResolver`** (id → name projections for just the page's ids). Don't go back to listing every specification, connection and scenario per page.
  - **`ICallRecordRepository.InsertAsync` completes only once the row is written** (`IDbWriteQueue.EnqueueAsync` awaits the job), so a record is readable as soon as the call that wrote it returns. No polling needed in tests.
- **Provider mode** (`Application/Mocking/Set*ProviderMode`, `InvokeMockEndpoint.ProviderMode`; contract-testing "Phase D").
  - On the provider port only operations with `ServeAtRealPath` answer; `/mock` keeps answering every enabled operation.
  - The incoming body is validated against `RequestSchema` in both modes, and the outcome only goes to the history. The response never depends on it. Only JSON content types (`application/json`, `*+json`) are validated. HEAD matches the GET operation.
  - `SetServeAtRealPathAsync` returns the rows it updated. Fewer than asked (a concurrent re-import removed the operation from the spec) must be a refusal (409), never a reported success.
  - Re-importing a spec must keep `ServeAtRealPath` for operations whose key is unchanged. That follows from the in-place update (`ApiSpecification.ApplyReimport`, see "Persistence" in `.claude/rules/infrastructure.md`); the import handlers don't carry it over themselves.
- **Enabling/disabling an operation** (`Application/Mocking/SetEndpointEnabled`, `PUT /api/mock-endpoints/{id}/enabled`, the "Mock enabled" switch on the operation card).
  - Only HTTP operations can be switched (AsyncAPI ones aren't served by the mock). A disabled operation answers 404 on both surfaces; its `ServeAtRealPath` is left as it was.
  - `SetEnabledAsync` returns the rows it updated, and fewer than asked is a 409 refusal, same as provider mode.
  - Re-importing a spec keeps `IsEnabled = false` for operations whose key is unchanged, through the same in-place update.
- **Response templating** (`IResponseTemplateEngine`, contract-testing "Phase F", see `docs/contract-testing-plan.md` 4.6).
  - `InvokeMockEndpointHandler` renders `ExampleTemplate` with a `TemplateContext` built from the match (`OperationKeyMatcher.TryMatch` gives the path parameters) and the request (`QueryParameters`, `Headers`, `Body`), and answers with `MockEndpoint.ExampleStatusCode ?? 200`. Keep that choice in the handler; the endpoint only maps `MockInvocationResult.StatusCode`/`ResponseBody` to HTTP.
  - A status that can't carry a body (204, 304) answers with an empty `ResponseBody`, and the endpoint writes no body for it. Don't render or send `"{}"` there: Kestrel refuses to write it.
  - An unfillable placeholder never fails the call. Its warning goes to `CallRecord.Warnings` (a JSON string array, null when there are none), next to `ValidationErrors`.
