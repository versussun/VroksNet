---
paths:
  - "tests/VroksNet.UnitTests/**"
---

# VroksNet.UnitTests

Everything that isn't a full-graph test: handler tests, parser tests, repository tests, and tests of real network-facing classes against unreachable targets. Shared rules: `.claude/rules/tests.md`.

## Rules

- **Handler tests use hand-written fakes** in `TestDoubles/` (e.g. for `IApiSpecificationRepository`/`ISpecificationParser`). Don't add a mocking library; the interfaces are small enough for plain stand-ins.
  - **Exception: `ISchemaValidator`.** Handler tests use the real `SchemaValidator`. It is pure, in-process and deterministic, and contract-validation tests are only meaningful against real JSON Schema evaluation.
- **Parser tests read fixture YAML from `Fixtures/`.** Mark files `CopyToOutputDirectory` and read them via `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)`.
- **Repository tests use a real temp-file SQLite database** through the actual `DbWriteQueue`/`DbWriteBackgroundService`. **Don't downgrade them to an in-memory fake or a fake queue**; that setup is what caught the replace-by-title bug.
- **A SQLite-backed test's `DisposeAsync` must call `SqliteConnection.ClearAllPools()` before deleting its temp `.db` file.** `Microsoft.Data.Sqlite` pools native connections, so the file can stay locked after every `DbContext` is disposed.
- **Test real network classes against unreachable targets:** definitely-closed local ports and `.invalid` hostnames (RFC 2606). These tests are fast and deterministic and need no Docker.
  - `Connections/ConnectionTesterTests.cs` exercises the real `ConnectionTester`.
  - `TestScenarios/MessageSenderTests.cs` exercises the real `MessageSender`.
- **Keep success-path coverage out of this project.** It belongs in `VroksNet.IntegrationTests` (see `.claude/rules/integration-tests.md`).

## Known gaps

- The Http round-trip success path of `MessageSender` is covered only end-to-end in IntegrationTests. RabbitMq/Nats success paths aren't covered end-to-end anywhere yet: that needs a real broker and a way to assert on what it received.
