# VroksNet.Web

Project-level rules. Solution-wide rules (dependency direction, coding rules) are in `.claude/CLAUDE.md`.

- `src/VroksNet.Web` — Blazor **WebAssembly** (standalone, `Microsoft.NET.Sdk.BlazorWebAssembly`) Admin UI. Calls the API over HTTP (typed `HttpClient`, e.g. `SpecificationApiClient`) — it is never a composition root, has no `Application`/`Infrastructure` references, and can't reference server-side project types anyway (WASM). Its `HttpClient.BaseAddress` comes from `wwwroot/appsettings.{Environment}.json` (`ApiService:BaseAddress`) — not Aspire's `https+http://` service-discovery scheme, which only resolves server-side and does nothing in browser-executed code. In dev it runs as its own process via `AppHost` for hot reload, hitting `ApiService`'s fixed dev URL with CORS enabled on `ApiService` for it; in Production it's build output only, served by `VroksNet.ApiService` (same origin, no CORS needed there).
- Presentation layer: see `src/VroksNet.ApiService/CLAUDE.md` for the shared Presentation rules (thin, no business logic).
- `VroksNet.Web`'s Razor components talk to the backend only over HTTP (typed `HttpClient` calling `VroksNet.ApiService`'s endpoints) — they never reach into `Application`/`Infrastructure`/`Domain` types directly, since a WASM client can't reference server-side project types anyway.
