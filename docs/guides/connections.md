# Connections

A **connection** is where a service or a broker lives: a type (HTTP, RabbitMQ, Kafka, …) and a value (a URL or a connection string). Test scenarios and Publishers go through a connection, by name.

There are four ways to add one:

| Way | Kept across restarts | Good for |
|---|---|---|
| [Admin UI](#in-the-admin-ui) | yes (in the database) | trying things out, one-off setups |
| [REST API](#through-the-api) | yes (in the database) | scripts |
| [Manifest `vroksnet.yaml`](#in-the-manifest-vroksnetyaml) | re-applied at every start | a reproducible setup together with specs, Publishers and scenarios |
| [Environment variables](#from-environment-variables) | re-applied at every start | containers, Compose, Aspire, CI; secrets kept out of files |

The manifest and environment variables are both **provisioning**: they're applied at startup, and can be used together.

## Types and values

| Type | Value | Example |
|---|---|---|
| `Http` | base URL (may include a path and query) | `https://orders.internal/api/v1` |
| `RabbitMq` | AMQP connection string | `amqp://user:password@rabbit:5672/vhost` |
| `Nats` | NATS URL | `nats://user:password@nats:4222` |
| `Kafka` | bootstrap servers, librdkafka `key=value;…` settings, or an Azure Event Hubs connection string | `kafka1:9092,kafka2:9092` |
| `Mqtt` | `mqtt://` URL, `mqtts://` for TLS | `mqtt://user:password@broker:1883` |
| `Redis` | StackExchange.Redis connection string, or a `redis://` / `rediss://` URL | `cache:6379,password=secret` |
| `ServiceBus` | Azure Service Bus shared-access connection string | `Endpoint=sb://shop.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…` |
| `Sqs`, `Sns` | `Region=…;AccessKeyId=…;SecretAccessKey=…`, or `Region=…` alone for the default AWS credentials; `ServiceUrl=http://…` for an emulator | `Region=eu-west-1;AccessKeyId=AKIA…;SecretAccessKey=…` |
| `Grpc` | URL; `http://` is HTTP/2 without TLS | `http://orders:5001` |

Each type's details (Kafka SASL/TLS, Event Hubs, Redis modes, Service Bus subscriptions, SQS/SNS permissions) are in [Getting started → Concepts](contract-testing/getting-started.md#concepts). `GET /api/system/connection-types` lists the types the running image supports.

Connection names are unique. Broker settings such as the RabbitMQ exchange, the Redis mode or the Service Bus subscription aren't part of a connection: they're `brokerOptions` on each test scenario and Publisher.

**Addresses are resolved where VroksNet runs.** In a container, `localhost` is the container itself. Use the broker's hostname on the shared Docker network (`kafka:9092`), or `host.docker.internal` for something running on the host (on Linux, add `--add-host=host.docker.internal:host-gateway` to `docker run`).

## In the Admin UI

1. Open **Settings**. Connections are listed in the **Connections** section.
2. Click **+ Add connection**, enter a name, pick the type, and paste the value. The placeholder shows the expected format, and **Show** reveals a hidden value.
3. **Test connection** checks the value before saving.
4. **Save**.

In the list, **Test** checks a saved connection, and **Edit** and **Delete** do what they say. A connection created by provisioning carries a **Provisioned** badge. You can edit it, but the next start brings it back in line with the manifest or variables.

## Through the API

```bash
API=http://localhost:8080

# Check a value without saving it
curl -s -X POST "$API/api/connections/test" -H "Content-Type: application/json" \
  -d '{ "serviceType": "Kafka", "value": "kafka:9092" }'
# → {"success":true,"message":"…"}

# Create
curl -s -X POST "$API/api/connections" -H "Content-Type: application/json" \
  -d '{ "name": "kafka", "serviceType": "Kafka", "value": "kafka:9092" }'
# → {"id":"8c1e…"}

# List, test, update, delete
curl -s "$API/api/connections"
curl -s -X POST "$API/api/connections/8c1e…/test"
curl -s -X PUT "$API/api/connections/8c1e…" -H "Content-Type: application/json" \
  -d '{ "name": "kafka", "serviceType": "Kafka", "value": "kafka1:9092,kafka2:9092" }'
curl -s -X DELETE "$API/api/connections/8c1e…"
```

A blank or taken name is a `400` with the reason in `detail`. All endpoints are in the [API quick reference](contract-testing/api-reference.md#connections).

## In the manifest (`vroksnet.yaml`)

Mount a directory at `/app/provisioning` with the manifest (and specs under `specs/`, if you have any). Each connection takes either `value` or `valueFrom`:

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/versussun/VroksNet/master/docs/schemas/provisioning-manifest.v1.schema.json
version: 1

connections:
  # The value as-is
  - name: orders-api
    type: Http
    value: http://orders:8080/api
  - name: grpc-orders
    type: Grpc
    value: http://orders:5001

  # The value from configuration: here the ConnectionStrings__rabbit variable
  - name: rabbit
    type: RabbitMq
    valueFrom: ConnectionStrings:rabbit
```

```bash
docker run -d --name vroksnet -p 8080:8080 -p 7353:7353 \
  -v vroksnet-data:/app/data \
  -v "$PWD/provisioning":/app/provisioning:ro \
  -e ConnectionStrings__rabbit=amqp://user:pass@rabbit:5672 \
  ghcr.io/versussun/vroksnet:latest
```

- **Keep secrets out of the file** with `valueFrom`. It names a configuration key, `Section:Key`, set as the variable `Section__Key`: `valueFrom: ConnectionStrings:rabbit` reads `ConnectionStrings__rabbit`.
- The schema is [`docs/schemas/provisioning-manifest.v1.schema.json`](../schemas/provisioning-manifest.v1.schema.json), also in the image at `/app/provisioning-manifest.v1.schema.json`. Editors that understand the `yaml-language-server` comment check the file as you type.
- The same manifest declares Publishers and test scenarios that use these connections by name. A full example: [`docs/samples/provisioning/`](../samples/provisioning/README.md).

## From environment variables

No files needed: each connection is four variables with an index, starting at 0.

| Variable | Required | What |
|---|---|---|
| `Provisioning__Connections__<i>__Name` | yes | the connection's name |
| `Provisioning__Connections__<i>__Type` | yes | a type from [the table above](#types-and-values), case-insensitive |
| `Provisioning__Connections__<i>__Value` | one of the two | the value as-is |
| `Provisioning__Connections__<i>__ValueFrom` | one of the two | a configuration key to read the value from, e.g. `ConnectionStrings:kafka` |

The key holds an index rather than the name because a name may contain `-`, which isn't allowed in variable names in POSIX shells.

### `docker run`

```bash
docker run -d --name vroksnet -p 8080:8080 -p 7353:7353 \
  -v vroksnet-data:/app/data \
  -e Provisioning__Connections__0__Name=kafka \
  -e Provisioning__Connections__0__Type=Kafka \
  -e Provisioning__Connections__0__Value=host.docker.internal:9092 \
  -e Provisioning__Connections__1__Name=orders-api \
  -e Provisioning__Connections__1__Type=Http \
  -e Provisioning__Connections__1__Value=http://host.docker.internal:5080/api \
  -e Provisioning__Connections__2__Name=rabbit \
  -e Provisioning__Connections__2__Type=RabbitMq \
  -e Provisioning__Connections__2__ValueFrom=ConnectionStrings:rabbit \
  -e ConnectionStrings__rabbit=amqp://user:pass@host.docker.internal:5672 \
  ghcr.io/versussun/vroksnet:latest
```

`kafka` and `orders-api` take their values directly. `rabbit` reads its value from `ConnectionStrings__rabbit`, so the secret can come from wherever your platform keeps secrets.

### Docker Compose

```yaml
services:
  vroksnet:
    image: ghcr.io/versussun/vroksnet:latest
    ports: ["8080:8080", "7353:7353"]
    volumes: ["vroksnet-data:/app/data"]
    environment:
      Provisioning__Connections__0__Name: kafka
      Provisioning__Connections__0__Type: Kafka
      Provisioning__Connections__0__Value: kafka:9092          # the service name on the Compose network
      Provisioning__Connections__1__Name: rabbit
      Provisioning__Connections__1__Type: RabbitMq
      Provisioning__Connections__1__ValueFrom: ConnectionStrings:rabbit
      ConnectionStrings__rabbit: ${RABBIT_CONNECTION_STRING}   # from .env or the shell
    depends_on: [kafka, rabbitmq]
  # kafka: …, rabbitmq: …

volumes:
  vroksnet-data:
```

### .NET Aspire

The Aspire package's `WithConnection(...)` sets these same variables: `WithConnection(name, resource)` sets `ValueFrom=ConnectionStrings:<resource>` together with the resource's connection string. The full mapping is in the [image contract §8](../container-contract.md).

## Combining the manifest and variables

- Connections from both sources are merged. The [provisioning sample](../samples/provisioning/README.md) declares `bookstore-http` in the manifest and `kafka` in variables.
- **The same name in both is an error.** Neither source silently wins.
- Variables alone are enough: with no `/app/provisioning` directory, provisioning still runs, and its report says `"source": "configuration"`.

## How provisioning treats connections

- **Applied at every start.** A provisioned connection is created, or updated to the declared type and value. A connection with the same name created earlier in the UI is taken over and becomes provisioned.
- **Edits in the UI last until the next start.** Change the manifest or the variables instead.
- **Removing a connection from the manifest or variables doesn't delete it.** Delete it in the UI if it's no longer needed.
- **Errors stop the container** with exit code `3` after logging each one, e.g. a missing `Type`, both or neither of `Value`/`ValueFrom`, or a `ValueFrom` key that isn't set. With `-e Provisioning__FailOnError=false` it keeps running with whatever did apply, and `/health` reports `Degraded`.

Check what was applied:

```bash
curl -s localhost:8080/api/system/info
```

```json
{
  "version": "0.4.1",
  "contractVersion": 1,
  "provisioning": {
    "status": "Applied",
    "source": "configuration",
    "appliedAt": "2026-10-14T13:40:00Z",
    "counts": { "specifications": 0, "connections": 3, "publishers": 0, "testScenarios": 0, "testSuites": 0 },
    "errors": []
  }
}
```

An error names the variable or the manifest entry it came from, and never contains the connection's value:

```json
{ "source": "Provisioning__Connections__2", "message": "ValueFrom \"ConnectionStrings:rabbit\" isn't set in the configuration." }
```

## From the UI to provisioning

Configure connections in the UI, then **Settings** → **Export** (or `GET /api/provisioning/export`) downloads `vroksnet-provisioning.zip`, with `specs/` and a `vroksnet.yaml` for the current state.

- Connection values aren't in it. Each connection becomes `valueFrom: ConnectionStrings:<name>`, and the file's header lists the `ConnectionStrings__<name>` variables to set.
- **Export with connection values** (`?inlineValues=true`) writes the values into the file instead. Keep such a file private.
