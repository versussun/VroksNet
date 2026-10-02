---
paths:
  - "src/VroksNet.AppHost/**"
---

# VroksNet.AppHost

Aspire orchestration only.

## Rules

- **No business logic in AppHost.** It only declares resources and wiring.
- **Keep `apiservice`'s HTTPS endpoint pinned to port `7352`:** `.WithHttpsEndpoint(port: 7352, name: "https")` in `AppHost.cs`. The port must match `ApiService:BaseAddress` in `src/VroksNet.Web/wwwroot/appsettings.Development.json`. If you change it, **update both places together**.
- **Keep the `provider` endpoint pinned to `7353` with `env: "Provider__Port"`, plus `Provider__PublicUrl=http://localhost:7353`.**
  - `Provider__Port` is how ApiService learns which (target) port is the provider one.
  - The pin gives services under test a stable URL.
  - `Provider__PublicUrl` is what the Admin UI shows. The target port behind Aspire's proxy isn't it.
- **Leave `webfrontend`'s port unpinned.** ApiService's dev CORS policy accepts any loopback origin. See `.claude/rules/api-service.md`.
- **Don't trust `launchSettings.json` ports under AppHost.** Check listening ports directly (`Get-NetTCPConnection`) when debugging connectivity.

## Message brokers

- Both RabbitMQ and NATS are in MVP scope (`docs/project-brief.md` §2). They are wired as Aspire resources (`AddRabbitMQ("rabbitmq")`, `AddNats("nats")`), and ApiService has matching clients (`Aspire.RabbitMQ.Client` → `AddRabbitMQClient("rabbitmq")`, `Aspire.NATS.Net` → `AddNatsClient("nats")`).
- The async-mock publish worker (`PublisherBackgroundService`, see "Publishers" in `.claude/rules/application.md`) publishes to broker *Connections* the user configures. It doesn't use these Aspire-wired clients, the same as `MessageSender`.
- These Aspire clients target the **dev-time broker resources only**. User-defined `Connection`s use their own clients. See "Connection testing" in `.claude/rules/infrastructure.md`.

## Gotchas

- **Dev-time ports under AppHost are random (verified by testing).** `dotnet run --project src/VroksNet.AppHost`, with or without a launch profile, gives `apiservice` and `webfrontend` a fresh random port every run. Per-project `launchSettings.json` `applicationUrl` is **not** honored. This broke Web → ApiService CORS calls until the pin above was added.
- **The pin works through Aspire's DCP proxy.** `dcp.exe` binds `7352` and forwards to ApiService's real random internal port, so ApiService's own process listening on a *different* port is expected. `7352` is the one that matters.
- **The DCP proxy does not run under `DistributedApplicationTestingBuilder`.** See `.claude/rules/e2e-tests.md`.
