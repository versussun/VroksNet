# VroksNet — Project Rules

> **Project brief:** product goal, MVP scope, architecture, roadmap and open questions live in `docs/project-brief.md` — read it before making product-scope decisions; this file governs code structure/style only.

.NET 10 / .NET Aspire solution. These rules govern how code is structured and written in this repo. Follow them by default; call out explicitly when a change would violate one.

## Solution layout

Each project carries its own `CLAUDE.md` with its role, constraints, and gotchas — read it before working in that project.

- `src/VroksNet.AppHost` — Aspire orchestration only. → `src/VroksNet.AppHost/CLAUDE.md`
- `src/VroksNet.ServiceDefaults` — shared cross-cutting Aspire wiring. → `src/VroksNet.ServiceDefaults/CLAUDE.md`
- `src/VroksNet.Domain` — Clean Architecture Domain layer. → `src/VroksNet.Domain/CLAUDE.md`
- `src/VroksNet.Application` — Clean Architecture Application layer; use cases and the Mediator conventions. → `src/VroksNet.Application/CLAUDE.md`
- `src/VroksNet.Infrastructure` — Clean Architecture Infrastructure layer (persistence, parsers, external clients). → `src/VroksNet.Infrastructure/CLAUDE.md`
- `src/VroksNet.ApiService` — the only server process / composition root. → `src/VroksNet.ApiService/CLAUDE.md`
- `src/VroksNet.Web` — Blazor WebAssembly Admin UI, talks to the API over HTTP only. → `src/VroksNet.Web/CLAUDE.md`
- `tests/VroksNet.UnitTests`, `tests/VroksNet.IntegrationTests`, `tests/VroksNet.E2ETests` — see "Testing" below.

## Architecture: Clean Architecture

Dependencies point inward only. Outer layers depend on inner layers; inner layers know nothing about outer ones.

```
Domain  ←  Application  ←  Infrastructure
                ↑
          Presentation (ApiService / Web)
```

- Never reference outward: Domain must not reference Application/Infrastructure/Presentation; Application must not reference Infrastructure or Presentation.
- Presentation (`VroksNet.ApiService`, `VroksNet.Web`) and everything else calls use cases only through Mediator — details in `src/VroksNet.Application/CLAUDE.md`.
- Only `VroksNet.ApiService` is a composition root; `VroksNet.Web` never is.
- Per-layer roles and constraints live in each project's own `CLAUDE.md` (see "Solution layout").

## Testing

All test-specific rules (fixtures, boot/lifecycle, Docker/Playwright requirements, xUnit idiom) live next to the tests, not here:

- `tests/CLAUDE.md` — rules shared by every test project (xUnit v3 idiom).
- `tests/VroksNet.UnitTests/CLAUDE.md` — handler/parser/repository unit tests.
- `tests/VroksNet.IntegrationTests/CLAUDE.md` — Aspire integration tests.
- `tests/VroksNet.E2ETests/CLAUDE.md` — Playwright browser E2E tests.

## .NET coding rules

- **Seal by default.** Every class is `sealed` unless it is explicitly designed as a base class for inheritance (i.e. it already has, or is deliberately meant to have, derived classes). If you're not designing for extension, seal it — this includes handlers, services, and DTOs.
- **Nullable reference types** enabled everywhere (already set per-project) — don't suppress warnings with `!` unless truly unavoidable; add a comment when you do.
- **File-scoped namespaces** (`namespace Foo.Bar;`) in all new files.
- **Prefer records** for immutable data (DTOs, Mediator requests/notifications, value objects). Prefer primary constructors for simple dependency injection into classes.
- **`async`/`await` all the way** — no `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` on async code; suffix async methods with `Async` outside of Mediator handlers (`Handle` keeps the interface's name).
- **One public type per file**, file name matches the type name.
- Favor expression-bodied members for trivial one-liners; keep everything else in a normal method body for readability.
- No business logic in Presentation projects — it belongs in Application (orchestration) or Domain (rules/invariants).
