---
paths:
  - "tests/VroksNet.IntegrationTests/**"
---

# VroksNet.IntegrationTests

Aspire integration tests. They boot the *real* distributed-application graph (`AppHost.cs` as-is: `apiservice`, `webfrontend`, and the broker containers `apiservice` waits on — RabbitMQ/NATS/Kafka for the core collection) via `Aspire.Hosting.Testing`, then drive it over the wire. Shared rules: `.claude/rules/tests.md`.

## Rules

- **Black-box only.** Don't reference Domain/Application/Infrastructure. Tests talk JSON/HTTP to the real endpoints, so they can't silently pass against an internal shape that doesn't match the wire contract.
- **Boot once per collection, not per test.** `Fixtures/AppHostFixtureBase.cs` (`IAsyncLifetime`) builds and starts the graph once, with only the brokers it's given (AppHost's `--Brokers=` setting). `Fixtures/AppHostFixture.cs` is the core one (RabbitMQ, NATS, Kafka), and `Fixtures/AppHostCollection.cs` (`[CollectionDefinition("AppHost")]` + `ICollectionFixture<AppHostFixture>`) shares it across every `[Collection("AppHost")]` test class.
- **A broker family added after the core three gets its own graph and CI job** (step R6 of `docs/broker-adapters-plan.md`), so it doesn't add minutes to every run:
  - AppHost: add its resource to `knownBrokers` and wire it like the others, inside `if (brokers.Contains("<name>"))`.
  - Tests: everything for it lives in `Brokers/<Family>/` (namespace `VroksNet.IntegrationTests.Brokers.<Family>`): a `sealed class <Family>AppHostFixture() : AppHostFixtureBase(["<name>"])`, its `[CollectionDefinition("AppHost.<Family>")]`, and the test classes — Send, Listen, and a suite whose Send is caught by its Listen.
  - CI: the required "Integration tests" job skips `VroksNet.IntegrationTests.Brokers.*`; add `<Family>` to the `broker-integration` matrix in `.github/workflows/ci.yml`.
  - The families so far are `Brokers/Mqtt` (N2), `Brokers/Redis` (N3) and `Brokers/ServiceBus` (N4, against the emulator) and `Brokers/Aws` (N5, LocalStack; its tests create their own GUID-named queues and topics). A family with a large image lists it under `images` in its matrix entry, which CI pulls before the tests — pulled during the fixture's 2-minute start, it can outlast it — copy one's fixture, collection and `<Family>ApiTests` for the next. `RedisAppHostFixture.ConnectionValueAsync` shows how to take an Aspire resource's connection string as the Connection value, which is what the Aspire package hands out.
  - Locally: `dotnet tests/VroksNet.IntegrationTests/bin/Debug/net10.0/VroksNet.IntegrationTests.dll --filter-namespace "VroksNet.IntegrationTests.Brokers.<Family>"`. A graph with no core brokers boots: `apiservice` doesn't need them (verified while adding this).
- **Don't assume isolated state between tests.** The collection runs sequentially against one `apiservice` process and its single-writer SQLite queue (see `.claude/rules/infrastructure.md`).
- **Use GUID-suffixed values for anything with a unique constraint** (e.g. `Connection.Name`), even though the DB is fresh each run. That keeps tests safe against a *persisted* DB if `ConnectionStrings__VroksNetDb` is set for debugging. Specification imports are safe to repeat either way (`UpsertAsync` replaces by `Title`).
- **Cover the success path of outbound sends here** (the `Connection` test, and `TestScenario` runs over `Http`) by pointing an `Http` connection back at the booted ApiService's own `/health`. Use `AppHostFixture.ApiServiceHttpAddress` (plain http), not `ApiServiceClient.BaseAddress` (https): the send happens server-side through `MessageSender`, which trusts the https dev certificate only where it has been trusted locally, never on CI. The E2E fixture exposes the same property. `AppHostFixture.ProviderAddress` is the provider port, and the fixture turns its CORS on for `AppHostFixture.AllowedProviderOrigin` (test graph only; AppHost leaves it off); tests that turn provider mode on should turn it off again, or use GUID-suffixed paths no other test can hit. Failure paths belong in `VroksNet.UnitTests`, except broker-side ones that need a real broker (e.g. publishing to a missing RabbitMQ exchange, `ListenScenarioApiTests`).

## Requirements

- **A running container runtime** (Docker Desktop or compatible). `apiservice` does `.WaitFor(rabbitmq).WaitFor(nats).WaitFor(kafka)`, so the graph can't boot without one. Skip this project where no container runtime is available.
- **The database is fresh every run.** `AppHost.cs` doesn't set `ConnectionStrings:VroksNetDb`, so `apiservice` gets the in-memory default.
