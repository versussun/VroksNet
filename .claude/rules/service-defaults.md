---
paths:
  - "src/VroksNet.ServiceDefaults/**"
---

# VroksNet.ServiceDefaults

Shared cross-cutting Aspire wiring: telemetry, health checks, resilience.

## Rules

- **Only cross-cutting infrastructure concerns go here.** No business logic and no feature-specific code.
- **`/health` and `/alive` are mapped in every environment**, not only Development. In Production an unmapped `/health` falls through to ApiService's SPA fallback and answers `200` with `index.html`. Keep the default response writer (overall status only, no per-check details): the endpoints are unauthenticated.
