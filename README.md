# VroksNet

[![CI](https://github.com/versussun/VroksNet/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/versussun/VroksNet/actions/workflows/ci.yml)
[![Publish image](https://github.com/versussun/VroksNet/actions/workflows/publish-image.yml/badge.svg?branch=master)](https://github.com/versussun/VroksNet/actions/workflows/publish-image.yml)

An internal mock server and contract-testing tool for **OpenAPI** and **AsyncAPI** specifications, in the spirit of Microcks, built on .NET 10 and .NET Aspire. One Docker image runs the API, the mocks and the Admin UI.

## What it does

- **REST mocks.** Import an OpenAPI spec, and each operation answers with the spec's example.
  - The example is templated: `{{request.path.id}}`, `{{request.body.name}}`, `{{uuid}}`, `{{now}}` and similar are filled in from the request.
  - The mock answers at the status the example came from.
  - Mocks live under `/mock/…`, or at the spec's **real paths** on a separate provider port, so a service can be pointed at the mock just by changing its host.
- **Async mocks.** **Publishers** send an AsyncAPI operation's example message to **RabbitMQ, NATS or Kafka** every 1s–24h, or on demand.
- **Contract testing**, run from the Admin UI or the REST API:

  | Type | What's checked |
  |---|---|
  | 1. Consumer (HTTP) | call a real service and validate its response against the spec |
  | 2. Producer (broker) | publish a message to a service through a broker |
  | 3. Provider (HTTP) | validate the requests a service sends to the mock |
  | 4. Provider (broker) | listen on a broker and validate what a service publishes |

- **Call history.** Every mock call, test run and publish is recorded with its bodies and contract-check result, and can be filtered.
- **Admin UI** (Blazor WebAssembly) for all of the above: specifications, connections, test scenarios, Publishers, call history.

Out of scope by design: authentication, multi-tenancy, clustering. VroksNet is meant to run inside a team's own infrastructure; see `docs/project-brief.md`.

## Quick start

### Docker

```bash
docker run -d --name vroksnet \
  -p 8080:8080 -p 7353:7353 \
  -v vroksnet-data:/app/data \
  ghcr.io/versussun/vroksnet:latest
```

- **Ports:** `8080` serves the API and the Admin UI (open `http://localhost:8080`); `7353` is the provider port, with the mocks at their real paths.
- **The image** supports `linux/amd64` and `linux/arm64` and runs as an unprivileged user.
- **Tags:** `latest` is the newest release, `X.Y.Z` / `X.Y` are releases, and `master` is the latest build of `master`. Pin a version (`:0.1.0`) for anything you depend on.

Import a sample spec and call its mock:

```bash
curl -X POST http://localhost:8080/api/specifications/openapi \
  -H "Content-Type: text/plain" -H "X-File-Name: petstore-openapi.yaml" \
  --data-binary @docs/samples/petstore-openapi.yaml

curl http://localhost:8080/mock/pets
```

### Development (.NET Aspire)

Needs the .NET 10 SDK and Docker.

```bash
dotnet dev-certs https --trust
dotnet run --project src/VroksNet.AppHost
```

This starts the API, the Admin UI and RabbitMQ, NATS and Kafka containers. The Aspire dashboard link is printed in the console:

| What | Where |
|---|---|
| Admin UI | the `webfrontend` resource in the dashboard |
| API | `https://localhost:7352` |
| Provider port | `http://localhost:7353` |
| Broker connection strings | the `rabbitmq` / `nats` / `kafka` resources in the dashboard |

See [Getting started](docs/guides/contract-testing/getting-started.md) for a guided first run.

## Supported formats

| | Supported |
|---|---|
| OpenAPI | Swagger 2.0, OpenAPI 3.0 and 3.1, YAML or JSON |
| AsyncAPI | 3.0, YAML or JSON (a 2.x file imports with no operations) |
| Brokers | RabbitMQ (`amqp://…`), NATS (`nats://…`), Kafka (`host:9092,…` or librdkafka `key=value;…` settings for SASL/TLS) |

Re-importing a spec with the same `info.title` updates it in place: operations keep their ids and settings, so test scenarios and Publishers on them keep working. Ready-to-import examples are in [`docs/samples/`](docs/samples/README.md).

## Configuration

The main settings, as environment variables. All of them are in the [runbook](docs/runbook.md).

| Variable | Default (Docker image) | What |
|---|---|---|
| `ConnectionStrings__VroksNetDb` | `Data Source=/app/data/vroksnet.db` | SQLite database. Unset means an in-memory database, wiped on restart |
| `Provider__Port` | `7353` | the provider port (mocks at real paths) |
| `Provider__PublicUrl` | — | the provider port's address as users see it, shown in the Admin UI |
| `Provider__CorsOrigins` | — (CORS off) | origins a browser app may call the provider port from, comma-separated or `*` |

## Project structure

Clean Architecture, orchestrated by .NET Aspire. Rules for each project are in `.claude/rules/`.

| Project | Role |
|---|---|
| `src/VroksNet.Domain` | entities and business rules, no framework references |
| `src/VroksNet.Application` | use cases (via [martinothamar/Mediator](https://github.com/martinothamar/Mediator)) and the interfaces Infrastructure implements |
| `src/VroksNet.Infrastructure` | EF Core + SQLite, spec parsers, broker and HTTP clients, background workers |
| `src/VroksNet.ApiService` | the only server process: REST API, mocks, and the Admin UI's static files in production |
| `src/VroksNet.Web` | the Admin UI, Blazor WebAssembly |
| `src/VroksNet.AppHost`, `src/VroksNet.ServiceDefaults` | Aspire orchestration and shared telemetry/health checks |
| `tests/` | unit, integration (real AppHost + brokers) and E2E (Playwright) tests |

## Development

```bash
dotnet build VroksNet.slnx
dotnet test tests/VroksNet.UnitTests          # fast, no Docker
dotnet test tests/VroksNet.IntegrationTests   # boots the AppHost with broker containers; needs Docker
dotnet test tests/VroksNet.E2ETests           # needs Docker and, once, Playwright's browsers:
                                              #   pwsh tests/VroksNet.E2ETests/bin/Debug/net10.0/playwright.ps1 install
```

CI runs all three suites on every pull request, and `master` only accepts pull requests that pass. Each merge to `master` publishes the image to GHCR, after `scripts/verify-container-contract.sh` has checked it against the [image contract](docs/container-contract.md) (run it locally on an image you built).

## Documentation

- **Using it**
  - [Contract testing guides](docs/guides/contract-testing/README.md) — getting started, a guide per test type, the API reference.
  - [Sample specifications](docs/samples/README.md) — ready-to-import specs and the import rules they rely on.
- **Running it**
  - [Runbook](docs/runbook.md) — deploying, configuring, backing up, upgrading and troubleshooting an instance.
  - [Image contract](docs/container-contract.md) — what code outside the container may rely on (ports, paths, variables, labels).
- **Design**
  - [Project brief](docs/project-brief.md) — product goals, scope and architecture.
  - [Contract-testing plan](docs/contract-testing-plan.md) — design and decisions behind contract testing.
  - [ADRs](docs/adr/) — accepted decisions: provisioning VroksNet as an Aspire resource, background and scheduled test runs.
  - [Implementation plan](docs/implementation-plan.md) — the next steps and their status.

## License

[MIT](LICENSE.txt)
