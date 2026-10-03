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

- **Which brokers start is configurable:** `Brokers` (comma-separated resource names, e.g. `--Brokers=rabbitmq,nats`; `--Brokers=` for none) — all of them when it isn't set, so `dotnet run` and the E2E tests always get every broker. The integration tests set it per collection (`.claude/rules/integration-tests.md`). An unknown name fails at startup. A new broker resource goes into `knownBrokers` and is wired inside its own `if (brokers.Contains(...))`. The E2E fixture passes the core three explicitly, so a broker added later costs the browser tests nothing.
- **`mqtt` is a plain Mosquitto container** (`eclipse-mosquitto:2` with `/mosquitto-no-auth.conf`, endpoint `mqtt` → 1883): there's no Aspire hosting integration for MQTT. `apiservice` only waits for it, since VroksNet reaches MQTT through user Connections.
- RabbitMQ and NATS are in MVP scope (`docs/project-brief.md` §2); Kafka was added after it (Phase 05). All three are wired as Aspire resources (`AddRabbitMQ("rabbitmq")`, `AddNats("nats")`, `AddKafka("kafka")`), and `apiservice` waits for each. ApiService has matching clients for the first two (`Aspire.RabbitMQ.Client` → `AddRabbitMQClient("rabbitmq")`, `Aspire.NATS.Net` → `AddNatsClient("nats")`). There's deliberately no `Aspire.Confluent.Kafka` client: nothing would use it, since Kafka is only ever reached through user `Connection`s.
- The async-mock publish worker (`PublisherBackgroundService`, see "Publishers" in `.claude/rules/application.md`) publishes to broker *Connections* the user configures. It doesn't use these Aspire-wired clients, the same as `MessageSender`.
- These Aspire clients target the **dev-time broker resources only**. User-defined `Connection`s use their own clients. See "Connection testing" in `.claude/rules/infrastructure.md`.

## Gotchas

- **Dev-time ports under AppHost are random (verified by testing).** `dotnet run --project src/VroksNet.AppHost`, with or without a launch profile, gives `apiservice` and `webfrontend` a fresh random port every run. Per-project `launchSettings.json` `applicationUrl` is **not** honored. This broke Web → ApiService CORS calls until the pin above was added.
- **The pin works through Aspire's DCP proxy.** `dcp.exe` binds `7352` and forwards to ApiService's real random internal port, so ApiService's own process listening on a *different* port is expected. `7352` is the one that matters.
- **The DCP proxy does not run under `DistributedApplicationTestingBuilder`.** See `.claude/rules/e2e-tests.md`.
