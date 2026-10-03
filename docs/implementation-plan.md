# Implementation plan — ADRs 0001 and 0002

**Status:** in progress. Done: step 0 (CI), A1 (image on GHCR), A2 (unique names), A3 (provisioning core), A4 (connections from variables, `/api/system/info`), A5 (provisioning badge, example), A6 (contract check in CI), B1 (background runs and history), B2 (cron scheduling), B3 (delayed runs).
**Covers:** ADR 0001 (`docs/adr/0001-aspire-integration-and-provisioning.md`) and ADR 0002 (`docs/adr/0002-background-and-scheduled-test-runs.md`). The ADRs hold the decisions and their reasoning; this document only orders the work.

Each step is one pull request that can be merged and shown on its own. Sizes: **S** — a day or so, **M** — a few days, **L** — a week or more.

## Step 0 — CI (S) — ✅ done

Every later step needs an automatic safety net; today there's no `.github` directory at all.

- GitHub Actions on pull requests and `master`: `dotnet build`, unit, integration and E2E tests. GitHub-hosted runners have Docker; E2E also needs the Playwright browsers installed.
- **Done when:** every PR gets a pass/fail check, and `master` requires it.

## Track A — provisioning (ADR 0001)

### A1. Publish the image to GHCR (S) — ✅ done

- A publish job on `master` and on `vX.Y.Z` tags: tags `X.Y.Z`, `X.Y`, `latest`.
- OCI labels, including `io.vroksnet.contract.version=1` (`docs/container-contract.md` §2).
- An unprivileged `USER` in the Dockerfile, owning `/app/data`. The image currently runs as root.
- **Done when:** `docker pull ghcr.io/versussun/vroksnet` works, the container runs as non-root, and data on the volume persists.
- **Risk:** existing volumes are owned by root. The runbook needs a note on fixing ownership when upgrading.

### A2. Unique names for Publishers and Test Scenarios (S) — ✅ done

- A migration adding unique indexes. Existing duplicates are renamed with a ` (2)` suffix before the index is created.
- Create/update validation: a taken name is a clear 400.
- **Why:** the manifest references objects by name. Prerequisite for A3.
- **Done when:** the migration succeeds on a database with duplicates (a repository test on real SQLite).

### A3. Provisioning core (L) — ✅ done

The largest step of track A.

- **Infrastructure:**
  - reads `/app/provisioning`: `specs/**`, with the kind detected by the root key, and `vroksnet.yaml`;
  - validates the manifest against `provisioning-manifest.v1.schema.json`, embedded in the assembly and also copied into the image.
- **Application:** a single `ApplyProvisioning` request. It reuses the existing use cases: spec import (already an idempotent update), plus upserts of connections, Publishers and scenarios by name.
- **Marking provisioned objects:** a `ProvisionedAt` flag (migration). Objects created in the UI are never touched.
- **`ProvisioningHostedService`**, registered after `DbWriteBackgroundService`.
- **A `provisioning` health check**, modeled on `DatabaseHealthCheck`: Unhealthy until provisioning is applied.
- **Errors:** with `Provisioning:FailOnError=true` (the default), exit code 3.
- **Done when:** starting again on the same test folder creates no duplicates, and a broken spec fails startup with a readable log.

### A4. Connections from environment variables + `/api/system/info` (S) — after A3 ✅

- `Provisioning__Connections__<i>__Name/Type/Value/ValueFrom`, merged with the manifest; the same name in both is an error.
- `GET /api/system/info` per `docs/container-contract.md` §6.
- **Done when:** a container is configured by variables alone, without a manifest.

### A5. Provisioning UI and docs (S) — after A3 ✅

- A "managed by provisioning" badge on provisioned objects.
- A `docs/samples/provisioning/` example and a runbook section.

### A6. Contract verification in CI (S) — after A1 and A4 ✅

- A job runs the built image per `docs/container-contract.md` §9:
  - with the specs from `docs/samples/`: checks `/health`, `/api/system/info` and the contract label;
  - with a deliberately broken spec: checks exit code 3.
- **Done when:** a contract-breaking change fails CI here.
- After this step the package repository has everything it needs: the container-to-container prototype and the package itself can start there.

### A7. Configuration export (M) — after A3; can be deferred

- `GET /api/provisioning/export` (a zip of the specs and a manifest, with secrets replaced by `valueFrom`) and an "Export" button.

## Track B — background and scheduled test runs (ADR 0002)

### B1. `TestRun` + background runs + history (L) — ✅ done

- **Domain:** a `TestRun` entity with its statuses and triggers.
- **Migration:** `TestRun` and `CallRecord.TestRunId`, plus an index on `(TestScenarioId, ScheduledFor)`.
- **`TestRunBackgroundService`:**
  - only schedules; runs go through the same `RunTestScenarioHandler`;
  - one active run per scenario, a global concurrency limit of 4;
  - cancellation through an in-memory token map;
  - runs left `Running` across a restart become `Interrupted`;
  - keeps the last 100 runs per scenario.
- **API:**
  - `POST /api/test-scenarios/{id}/runs` → `202`;
  - `GET /api/test-runs`, `GET /api/test-runs/{id}`;
  - `POST /api/test-runs/{id}/cancel`;
  - the synchronous `/run` works as before, but also records a `TestRun`.
- **Listen in the background** waits up to 30 minutes.
- **UI:** a "Run in background" button, a run-history tab, status refreshed by polling.
- **Done when:** a run survives closing the tab, a Listen waits longer than 80s, and a restart mid-run yields `Interrupted`.

### B2. Cron scheduling (M) — after B1 ✅

- `ICronSchedule` in Application, implemented in Infrastructure with Cronos (pinned version).
- A cron expression and a time zone on `TestScenario` (migration), validated with a clear 400.
- Anchored on `ScheduledFor`; runs missed while the app was down aren't caught up.
- **UI:** "Schedule" and "Time zone" fields showing the next run times, and a badge in the list.
- **Done when:** `*/1 * * * *` runs a scenario once a minute, and a daylight-saving transition in `Europe/Kyiv` is covered by a unit test.

### Later

- **B3.** Delayed one-off runs (S). ✅ `runAt`/`delaySeconds` on `POST …/runs` (up to 30 days ahead), "Run later" in the UI, and Cancel for waiting runs in the history.
- **B4.** Suites for CI (M).
- **B5.** `testSuites` and `runOnStartup` in the manifest (S) — after A3 and B4.

## Order

```
Step 0 (CI)
├── Track A: A1 ─┐
│                ├─ A6 ── (package repository: prototype → package)
│   A2 → A3 → A4 ┘
│          ├── A5
│          └── A7 (can be deferred)
└── Track B: B1 → B2 → (later: B3, B4 → B5 ← A3)
```

Recommended sequence for a single developer: **0 → A1 → B1 → A2 → A3 → B2 → A4 → A5 → A6 → A7**.

- **A1 comes early:** the container-to-container prototype — ADR 0001's main technical risk — can't start in the package repository until an image is published.
- **B1 comes next:** it removes the problem the team feels most, the 80-second Listen cap.

## Risks to check first

- **Container-to-container addresses for Kafka** (two listeners). Verified with a prototype in the package repository right after A1, before A3.
- **Ownership of existing volumes** after the switch to a non-root user in A1.
- **The unique-name migration in A2** on persistent databases that already have duplicates.
