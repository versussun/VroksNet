---
paths:
  - "tests/**"
---

# Tests: shared rules

Applies to every test project under `tests/`. Per-project rules:

- `.claude/rules/unit-tests.md`: handlers, parsers, repositories, real-class network tests.
- `.claude/rules/integration-tests.md`: the Aspire `AppHost` graph driven over HTTP.
- `.claude/rules/e2e-tests.md`: Playwright browser tests against `webfrontend`.

## Rules (xUnit v3)

- **Pass `TestContext.Current.CancellationToken` to every cancellable call in a test body**, not `CancellationToken.None`. The analyzer enforces this (`xUnit1051`).
- **Exception: lifecycle code.** In constructors and `IAsyncLifetime.InitializeAsync`/`DisposeAsync`, no test context exists yet, so `CancellationToken.None` is correct there.
