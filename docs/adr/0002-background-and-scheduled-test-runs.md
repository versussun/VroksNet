# ADR 0002 — Background, delayed and scheduled test runs

**Status:** Accepted (2026-10-02); open questions resolved 2026-10-03
**Date:** 2026-10-02
**Related:** `docs/contract-testing-plan.md` (test kinds, Test Scenarios), ADR 0001 (provisioning at startup), `.claude/rules/infrastructure.md` ("Publishers (async-mock worker)")

## Context

A Test Scenario can only be run synchronously today: `POST /api/test-scenarios/{id}/run` holds the HTTP request open until the run finishes, and `RunTestScenarioHandler` returns the result in the response. That means:

1. **A run lives only as long as the request.** Closing the tab or pressing Stop cancels the request, and the cancellation token reaches `MessageSender`/`MessageListener`. There's no way to start a test and come back later.
2. **Listen is capped at 80 seconds** (`TestScenarioListening.MaxTimeoutSeconds`). The run, plus up to 10s to reach the broker, must fit inside the Admin UI's HTTP client timeout (100s). An event the service publishes every 5 minutes can't be waited for.
3. **There's no run history.** A `TestScenario` keeps only its last result (`LastRunAt`, `LastRunSuccess`, `LastRunMessage`). Call History shows individual calls, but that's a traffic log, not test results: it can't answer "when did this contract break".
4. **Only a person can start a test.** There's no run at a given time, on a schedule, or automatically after startup — and the last one is what ADR 0001's scenario needs: start the AppHost in CI → tests run → collect the result.

Publishers already solve a similar problem: once a second, `PublisherBackgroundService` finds the publishes that are due and executes them through the same `PublishNow` the UI button uses. Background test runs should follow the same pattern.

## Decision

### A run as an entity of its own — `TestRun`

A new entity in `VroksNet.Domain.TestScenarios`: one run of one scenario.

| Field | Meaning |
|---|---|
| `Id`, `TestScenarioId` | no FK constraint, like the scenario's other references: a deleted scenario leaves its history behind |
| `Status` | `Queued → Running → Passed / Failed / Cancelled / Interrupted` |
| `Trigger` | `Manual`, `Delayed`, `Schedule`, `Startup`. (Implementation note, B1: the originally listed `Api` was folded into `Manual` — a run someone starts from the UI and one started through the API are the same action. Each later trigger is added with its step.) |
| `ScheduledFor` | when the run should start; for an immediate run, the time it was queued |
| `StartedAt`, `FinishedAt` | `DateTimeOffset`, stored as UTC ticks — the rule for fields sorted on in SQL |
| `Message`, `ContractValidation` | what `RunTestScenarioResult` returns today |
| `SuiteRunId` | set when the run is part of a suite (see below) |

`CallRecord` gets an optional `TestRunId`, so a run links to its traffic.

`LastRun*` on `TestScenario` stays, for showing the list quickly, and is updated from the latest finished `TestRun`.

### The executor — `TestRunBackgroundService`

Lives in `Infrastructure/TestRuns` and works like `PublisherBackgroundService`:

- **It only schedules.** Once a second it sends `ListDueTestRuns(now)` and then `ExecuteTestRun(id)` for each due run, through Mediator. The run logic stays in `RunTestScenarioHandler`, called by both the synchronous path and the worker. "The worker only schedules" is already the rule for Publishers.
- **One active run per scenario.** A new run of a scenario whose previous run is still going waits in the queue.
- **A global concurrency limit** — `TestRuns:MaxConcurrency`, default 4: ten Listens mustn't open ten broker connections at once.
- **Cancellation.** The worker keeps an in-memory `runId → CancellationTokenSource` map that `POST /api/test-runs/{id}/cancel` uses. In memory is enough: there's one process, and clustering is outside the brief.
- **Restart.** On startup, runs left `Running` are marked `Interrupted` ("interrupted by restart") instead of hanging forever. `Queued` runs keep waiting for their time.
- **Errors.** A failing run is logged and recorded in its result, and never escapes the loop, as with Publishers.

### API

| Method | What it does |
|---|---|
| `POST /api/test-scenarios/{id}/run` | **unchanged**: a synchronous run, kept for backward compatibility; it now also creates a `TestRun` with `Trigger = Manual` |
| `POST /api/test-scenarios/{id}/runs` | a background run → `202 { "runId" }`. Optional `runAt` (a time) or `delaySeconds` make it a delayed run (`Trigger = Delayed`) |
| `GET /api/test-runs?scenarioId=&status=&from=&to=` | history, paged |
| `GET /api/test-runs/{id}` | one run's status and result |
| `POST /api/test-runs/{id}/cancel` | cancellation: `Queued` → `Cancelled` immediately, `Running` → through its token |

### Listen without the 80-second cap

The cap came from the HTTP client. For background runs `MaxTimeoutSeconds` rises to 30 minutes (`TestRuns:MaxListenTimeoutSeconds`). The synchronous `/run` keeps the 80s, since it still holds the request. The UI only starts a Listen with a timeout over 80s in the background.

### Scheduling

A scenario gets an optional **cron** schedule. Decided: cron from the start, with no interval-only first step — teams need schedules like "weekdays at 09:00", which an interval can't express.

- **Syntax:** standard 5-field cron (minute, hour, day of month, month, day of week), e.g. `*/10 * * * *` or `0 9 * * 1-5`. No seconds field: the finest schedule is once a minute. That also bounds the load on services under test, so no separate minimum interval is needed.
- **Time zone:** each scenario has an optional IANA zone (`Europe/Kyiv`), default UTC. "09:00 on weekdays" only means something in a zone, and daylight-saving transitions follow the zone's rules.
- **Where the code lives:** `TestScenario` stores the expression and zone as plain strings. Computing the next occurrence sits behind an Application interface (`ICronSchedule`), implemented in Infrastructure with a small dependency-free parser (Cronos — MIT; it's what Hangfire uses), pinned in `Directory.Packages.props` and referenced only there. Domain stays free of third-party libraries.
- **Validation:** an invalid expression or unknown zone is rejected on create/update with the reason (a 400, like Publishers).

The schedule is anchored on the `ScheduledFor` of the last scheduled run: the next run is the cron's next occurrence after that planned time, not after the actual start time (which is what `LastPublishedAt` is for Publishers). That way the schedule doesn't drift by the worker's one-second tick or by queueing behind a full concurrency limit. The worker creates the next `TestRun` itself, with `Trigger = Schedule`. Runs missed while the app was down are **not caught up**: after a restart only the next one is queued.

### Suites

A `TestSuite` is a named list of scenarios. Running a suite creates a `SuiteRun` and one `TestRun` per scenario:

- **Order:** all Listens first, and only once their subscriptions are ready, all Sends. That way a suite checks the whole chain "sent a command → the service handled it → published an event → we caught it".
- **Outcome:** `Passed` only if every run passed.
- **For CI:** `GET /api/test-suites/{name}/runs/latest` → `{ status, failed: [...] }`. A pipeline script polls until the status is final and exits non-zero on `Failed`. That covers the brief's "CI integration" item without a separate CLI.

### Relation to ADR 0001

The provisioning manifest gains suites and running at startup:

```yaml
testSuites:
  - name: payments-contract
    scenarios: [payments-get, payments-refund, order-created-listen]
    runOnStartup: true          # Trigger = Startup, right after provisioning succeeds
    schedule: { cron: "*/10 * * * *", timeZone: Europe/Kyiv }
```

A startup suite that's still running doesn't affect `/health`: "ready" means "the mocks are loaded", not "the tests passed". The result is collected through the suite API.

### History retention

- **The last N runs per scenario are kept** — `TestRuns:RetentionPerScenario`, default 100. The same worker deletes the rest in the background, through `IDbWriteQueue` (writes must never bypass the queue).
- **An index on `(TestScenarioId, ScheduledFor)`** — for a scenario's history and for selecting due runs.

### UI

- **Test Scenarios:**
  - "Run in background" next to "Run";
  - "Schedule" (cron) and "Time zone" fields in the form, showing the next few run times so a mistyped expression is visible before saving;
  - a schedule badge in the list (e.g. `0 9 * * 1-5 · Europe/Kyiv`), with the next run time.
- **A new run-history tab per scenario:** status, trigger, duration, a link to the traffic in Call History.
- **A running run's status** is refreshed by polling `GET /api/test-runs/{id}` every 1–2s. No push channel (SignalR/SSE) is needed while polling copes.

## Options considered

### A. Just raise the Admin UI's HTTP client timeout

Fixes the 80s cap and nothing else: still no history, no delayed or scheduled runs, and a run still dies with the tab. Long-held requests also get cut by proxies and load balancers. **Rejected.**

### B. An external scheduler (CI cron, Kubernetes CronJob) calling the synchronous `/run`

- **Pros:** nothing changes in VroksNet.
- **Cons:**
  - still no history;
  - the 80s cap stays;
  - every team needs its own infrastructure for a simple job.

**Rejected** as the primary path. With the background API such a scheduler remains possible (`Trigger = Api`).

### C. Hangfire / Quartz.NET

- **Pros:** ready-made queues, cron, retries, a dashboard.
- **Cons:**
  - a heavy dependency with its own storage schema next to our SQLite, whose writes we deliberately serialize through `IDbWriteQueue` — a second writer breaks that guarantee;
  - a second dashboard;
  - overkill at the scale of "dozens of scenarios".

A custom worker modeled on `PublisherBackgroundService` is already proven. **Rejected.**

### D. A schedule field on `TestScenario` without a `TestRun` entity

Cheaper, but with no history and no way to cancel an individual run, and CI suites become impossible to express. **Rejected.**

## Consequences

**Positive**
- Tests survive closing the tab, and a Listen can wait up to 30 minutes.
- It's visible when a contract broke: history plus scheduling gives contract monitoring.
- Suites and `runOnStartup` give a CI workflow without a separate CLI.
- The run logic is still in one place — `RunTestScenarioHandler`.

**Negative and risks**
- **New tables and migrations** (`TestRun`, `TestSuite`, `SuiteRun`, `CallRecord.TestRunId`) and background history cleanup.
- **Another worker reading the database every second**, alongside `PublisherBackgroundService`. Unnoticeable at dozens of scenarios. If it becomes noticeable, merge both workers into one scheduler.
- **Load on services under test from scheduled runs.** Cron's one-minute granularity and the global concurrency limit bound it but don't rule it out. Needs documenting in the runbook.
- **A new dependency** for cron parsing (Cronos), confined to Infrastructure behind `ICronSchedule`.
- **The in-memory cancellation map** only works with a single process. If horizontal scaling ever arrives (currently outside the brief), cancellation has to move into the database.

## Implementation plan

1. **`TestRun` + background runs + history + cancellation.** Migration, `ExecuteTestRun`, `TestRunBackgroundService`, `POST /runs`, `GET /test-runs`, the history tab, `Interrupted` on restart, the 30-minute Listen limit for background runs, retention of the last 100 runs per scenario.
2. **Cron scheduling** — the trigger the team needs first: `ICronSchedule` + Cronos, schedule and zone on `TestScenario` (migration), validation, the form fields and the badge.

Later — not needed first:

3. **Delayed runs** (`runAt`/`delaySeconds`).
4. **Suites**: `TestSuite`, "Listens first" ordering, `GET …/runs/latest` for CI.
5. **`runOnStartup` and `testSuites` in the manifest** — after ADR 0001's layer 1 and step 4.

Failure notifications aren't planned (see below).

## Resolved questions

Answered on 2026-10-03:

1. **Which triggers come first?** Background runs and history (step 1), then **scheduling**. Delayed runs, suites and `runOnStartup` come later.
2. **Intervals or cron?** **Cron from the start**, with a per-scenario time zone; no interval-only step. See "Scheduling".
3. **How much history to keep?** The last **100 runs per scenario** (`TestRuns:RetentionPerScenario`).
4. **Failure notifications?** **Not now.** Failures are visible in the UI and through the API; webhook/Slack/email can be revisited once history and scheduling are in use.
