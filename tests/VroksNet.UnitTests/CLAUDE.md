# VroksNet.UnitTests

Everything that isn't a full-graph test: handler tests, parser tests, repository tests, and tests of real network-facing classes against unreachable targets. Shared xUnit rules are in `tests/CLAUDE.md`.

- **Handler tests** use fakes for `IApiSpecificationRepository`/`ISpecificationParser` in `TestDoubles/` — no mocking library, plain hand-written stand-ins are enough for interfaces this small.
- **Parser tests** run against fixture YAML files under `Fixtures/` (`CopyToOutputDirectory`, read via `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)`).
- **Repository tests** run against a real temp-file SQLite database through the actual `DbWriteQueue`/`DbWriteBackgroundService` (not a fake queue) — that's what caught the replace-by-title bug in the first place, so don't downgrade it to an in-memory fake.
- **A SQLite-backed test's `DisposeAsync` must call `SqliteConnection.ClearAllPools()`** before deleting its temp `.db` file — `Microsoft.Data.Sqlite` pools the native connection by default, so the file can still be locked immediately after every `DbContext` using it has been disposed.
- **`Connections/ConnectionTesterTests.cs`** exercises the *real* `ConnectionTester` (not a fake) against definitely-closed local ports and a `.invalid` (RFC 2606) hostname — fast and deterministic, no Docker needed, unlike the success-path coverage in `VroksNet.IntegrationTests` (which points an `Http` connection back at the booted ApiService's own `/health`).
- **`TestScenarios/MessageSenderTests.cs`** exercises the *real* `MessageSender` the same way — closed local ports / a `.invalid` hostname, no Docker needed. The Http round-trip success path is only covered end-to-end in `VroksNet.IntegrationTests` (same self-referential `/health` trick); RabbitMq/Nats success paths aren't covered end-to-end anywhere yet (would need a real broker + a way to assert on what it received).
