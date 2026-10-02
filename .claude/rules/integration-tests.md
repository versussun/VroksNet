---
paths:
  - "tests/VroksNet.IntegrationTests/**"
---

# VroksNet.IntegrationTests

Aspire integration tests. They boot the *real* distributed-application graph (`AppHost.cs` as-is: `apiservice`, `webfrontend`, and the RabbitMQ/NATS containers `apiservice` waits on) via `Aspire.Hosting.Testing`, then drive it over the wire. Shared rules: `.claude/rules/tests.md`.

## Rules

- **Black-box only.** Don't reference Domain/Application/Infrastructure. Tests talk JSON/HTTP to the real endpoints, so they can't silently pass against an internal shape that doesn't match the wire contract.
- **Boot once per run, not per test.** `Fixtures/AppHostFixture.cs` (`IAsyncLifetime`) builds and starts the graph once. `Fixtures/AppHostCollection.cs` (`[CollectionDefinition("AppHost")]` + `ICollectionFixture<AppHostFixture>`) shares it across every `[Collection("AppHost")]` test class.
- **Don't assume isolated state between tests.** The collection runs sequentially against one `apiservice` process and its single-writer SQLite queue (see `.claude/rules/infrastructure.md`).
- **Use GUID-suffixed values for anything with a unique constraint** (e.g. `Connection.Name`), even though the DB is fresh each run. That keeps tests safe against a *persisted* DB if `ConnectionStrings__VroksNetDb` is set for debugging. Specification imports are safe to repeat either way (`UpsertAsync` replaces by `Title`).
- **Cover the success path of outbound sends here** (the `Connection` test, and `TestScenario` runs over `Http`) by pointing an `Http` connection back at the booted ApiService's own `/health`. Use `AppHostFixture.ApiServiceHttpAddress` (plain http), not `ApiServiceClient.BaseAddress` (https): the send happens server-side through `MessageSender`, which trusts the https dev certificate only where it has been trusted locally, never on CI. The E2E fixture exposes the same property. Failure paths belong in `VroksNet.UnitTests`.

## Requirements

- **A running container runtime** (Docker Desktop or compatible). `apiservice` does `.WaitFor(rabbitmq).WaitFor(nats)`, so the graph can't boot without one. Skip this project where no container runtime is available.
- **The database is fresh every run.** `AppHost.cs` doesn't set `ConnectionStrings:VroksNetDb`, so `apiservice` gets the in-memory default.
