# Tests — shared rules

Rules that apply to every test project under `tests/`. Per-project rules live in that project's own `CLAUDE.md`:

- `VroksNet.UnitTests/CLAUDE.md` — handlers, parsers, repositories, real-class network tests.
- `VroksNet.IntegrationTests/CLAUDE.md` — Aspire `AppHost` graph driven over HTTP.
- `VroksNet.E2ETests/CLAUDE.md` — Playwright browser tests against `webfrontend`.

Project rules for production code (layering, Mediator, sealed classes, etc.) are in `.claude/CLAUDE.md`.

## xUnit v3 idiom

Pass `TestContext.Current.CancellationToken` to every cancellable call in a test body (analyzer-enforced, `xUnit1051`), not `CancellationToken.None`. Constructor/dispose lifecycle code (`IAsyncLifetime.InitializeAsync`/`DisposeAsync`) is the one exception — no test context exists yet at that point, so `CancellationToken.None` there is correct.
