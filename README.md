# VroksNet

An internal mock server and contract-testing tool for OpenAPI and AsyncAPI specifications, in the spirit of Microcks, built on .NET 10 and .NET Aspire.

- **Mocks:** import a spec and its operations answer with the spec's examples, either under `/mock/…` or at their real paths on a dedicated provider port.
- **Contract tests:** call a service and validate its responses, publish to it, listen to what it publishes, or check the requests it sends to a mock. Every call is recorded in a searchable call history.

## Documentation

- [Contract testing guides](docs/guides/contract-testing/README.md): getting started, and a step-by-step guide with examples for each of the four test types.
- `docs/project-brief.md` (Russian): product goals, scope and architecture.
- `docs/contract-testing-plan.md` (Russian): design and decisions behind contract testing.

## Run it

```bash
dotnet run --project src/VroksNet.AppHost      # development: API, Admin UI, RabbitMQ and NATS via Aspire
```

```bash
docker build -t vroksnet .
docker run -d -p 8080:8080 -p 7353:7353 -v vroksnet-data:/app/data vroksnet   # API + Admin UI on 8080, provider port on 7353
```

See [Getting started](docs/guides/contract-testing/getting-started.md) for details.
