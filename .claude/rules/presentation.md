---
paths:
  - "src/VroksNet.ApiService/**"
  - "src/VroksNet.Web/**"
---

# Presentation layer (ApiService + Web)

Shared rules for both Presentation projects. Project-specific rules: `.claude/rules/api-service.md`, `.claude/rules/web.md`.

## Rules

- **Keep it thin.** Endpoints and components only map HTTP/UI concerns to Mediator requests and map the results back to responses. Put no business logic here. It belongs in Application (orchestration) or Domain (rules/invariants).
- **Dispatch only through Mediator:** `IMediator.Send(...)` / `IMediator.Publish(...)`. Never call a handler directly.
- **Request-handling code reaches into Application only.** ApiService may reference Infrastructure solely to wire up DI at startup (composition root).
- **Web never touches server-side types.** Razor components talk to the backend only over HTTP, through a typed `HttpClient` calling ApiService endpoints.
