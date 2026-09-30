---
paths:
  - "tests/VroksNet.E2ETests/**"
---

# VroksNet.E2ETests

Playwright browser E2E tests. They boot the same real `AppHost` graph as `VroksNet.IntegrationTests` and then drive `webfrontend` through a real Chromium instance, which catches a Blazor component wired up wrong even when the API underneath is fine. Shared rules: `.claude/rules/tests.md`.

Everything in `.claude/rules/integration-tests.md` applies here too: one shared boot per run, a container runtime is required, the DB is fresh each run, and unique values get a GUID suffix.

## Rules

- **Share one Playwright `IBrowser` per run.** `Fixtures/AppHostFixture.cs` launches it in the same `InitializeAsync` as the graph and exposes `WebBaseAddress` (`App.GetEndpoint("webfrontend")`) to navigate against.
- **Every test class extends `Fixtures/PageTestBase.cs`.** It opens a fresh `IBrowserContext`/`IPage` per test in `InitializeAsync`/`DisposeAsync`, so each test gets its own cookies and local storage while reusing the shared browser.
- **Prefer `Page.GetByRole`/`GetByLabel`/`GetByText` over CSS selectors.** If a component lacks real `<label for>`/ARIA structure, **fix the component** (see `Settings.razor`'s `for`/`id` pairs) rather than reaching for a brittle CSS locator.
- **Keep the `appsettings.Development.json` rewrite in `AppHostFixture`** (see Gotchas). Don't replace it with an environment-specific appsettings file.

## Setup

- **Install Playwright browsers once:** build the project, then run `bin/Debug/net10.0/playwright.ps1 install` (add `--with-deps` on a fresh Linux CI box). Repeat only when the `Microsoft.Playwright` pin in `Directory.Packages.props` changes.
- **Headless by default.** Set `HEADED=1` locally to watch a test run.

## Gotchas

- **Under `DistributedApplicationTestingBuilder`, the WASM app can't reach `apiservice` without a fixup.** `Web/wwwroot/appsettings.Development.json` hardcodes `https://localhost:7352`, which only works through the DCP proxy of a real `dotnet run` AppHost (see `.claude/rules/apphost.md`). The testing builder doesn't proxy: nothing listens on `7352` and `apiservice` gets a genuine random port.
  - **Symptom:** every `fetch()` fails with `net::ERR_CONNECTION_REFUSED` (visible via `Page.RequestFailed` or the browser console), while the page only shows `TypeError: Failed to fetch`. Create-then-list tests break while pure-navigation tests (`HomeNavigationTests`) still pass, which makes it easy to miss.
  - **Fix in place:** after `App.StartAsync()`, `AppHostFixture.InitializeAsync` resolves `App.GetEndpoint("apiservice", "https")`, overwrites `wwwroot/appsettings.Development.json` with it, and restores the original in `DisposeAsync`. This runs once per run, before any test navigates.
  - **Why not use a separate `appsettings.{Environment}.json`?** It was tried and has no effect. In .NET 10, standalone Blazor WASM's environment name is a **build-time** MSBuild property (`WasmApplicationEnvironmentName`), and `ASPNETCORE_ENVIRONMENT` on the dev-server process (`.WithEnvironment(...)`, which worked in .NET 8/9) is no longer read at runtime.
