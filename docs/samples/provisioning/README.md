# Provisioning example

Starts VroksNet already configured: two sample specs imported, connections, a Publisher, two
test scenarios and a test suite created — and the suite run once at startup. How provisioning works: [ADR 0001](../../adr/0001-aspire-integration-and-provisioning.md),
[container contract](../../container-contract.md) §4–§7, [runbook](../../runbook.md#provisioning).

| Source | What it gives |
|---|---|
| `specs/bookstore-openapi.yaml` | the `Bookstore Sample API` REST mock |
| `specs/shop-events-kafka-asyncapi.yaml` | the `Shop Events Kafka Sample` events |
| [`vroksnet.yaml`](vroksnet.yaml) | the `bookstore-http` connection, a disabled operation, the `order-created` Publisher (off), the `list-books` (scheduled every 15 minutes) and `payments-settled` scenarios, the `smoke` suite (run at startup) |
| `Provisioning__Connections__0__*` | the `kafka` connection — declared by environment variables, the way the Aspire package passes `WithConnection(...)` |

## Run it

From the repository root:

```bash
docker run --rm -p 8080:8080 \
  -v "$PWD/docs/samples/bookstore-openapi.yaml":/app/provisioning/specs/bookstore-openapi.yaml:ro \
  -v "$PWD/docs/samples/shop-events-kafka-asyncapi.yaml":/app/provisioning/specs/shop-events-kafka-asyncapi.yaml:ro \
  -v "$PWD/docs/samples/provisioning/vroksnet.yaml":/app/provisioning/vroksnet.yaml:ro \
  -e Provisioning__Connections__0__Name=kafka \
  -e Provisioning__Connections__0__Type=Kafka \
  -e Provisioning__Connections__0__Value=host.docker.internal:9092 \
  ghcr.io/versussun/vroksnet:latest
```

The specs and the manifest are mounted file by file, as the Aspire package does; mounting a
whole directory at `/app/provisioning/specs` works as well.

## Check it

```bash
curl -s localhost:8080/health             # 503 while applying, then Healthy
curl -s localhost:8080/api/system/info    # "status":"Applied", counts 2 / 2 / 1 / 2 / 1
curl -s localhost:8080/api/test-suites/smoke/runs/latest   # the startup run: "status":"Passed"
```

In the Admin UI (<http://localhost:8080>) the provisioned objects carry a **Provisioned** badge.
**Run** on `list-books` passes against the container's own mock. `payments-settled` and the
`order-created` Publisher need a Kafka broker at the `kafka` connection's address.

Edits to provisioned objects last until the next start, which brings them back in line with the
files. Objects you create in the UI aren't touched.

## Variations

- **Value from another variable:** `-e Provisioning__Connections__0__ValueFrom=ConnectionStrings:kafka -e ConnectionStrings__kafka=broker:9092` instead of `__Value` — what Aspire's `WithReference(kafka)` sets.
- **Keep running on errors:** `-e Provisioning__FailOnError=false`. By default a provisioning error stops the container with exit code `3`, after logging each error.
- **Declare `kafka` in both places** (manifest and variables) and the start fails: the same name in both sources is an error, neither silently wins.
