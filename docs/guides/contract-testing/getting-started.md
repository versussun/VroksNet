# Getting started

[← Contract testing guides](README.md)

## Run VroksNet

### Locally, with .NET Aspire (development)

```bash
dotnet run --project src/VroksNet.AppHost
```

This starts the API, the Admin UI, and RabbitMQ, NATS and Kafka containers (Docker must be running). Open the Aspire dashboard link printed in the console. The `webfrontend` resource's URL there is the Admin UI.

| What | Address |
|---|---|
| API | `https://localhost:7352` |
| Admin UI | the `webfrontend` URL in the Aspire dashboard |
| Provider port (Type 3 mock) | `http://localhost:7353` |
| RabbitMQ / NATS / Kafka | connection strings in the Aspire dashboard (`rabbitmq` / `nats` / `kafka` resources) |

### In Docker

```bash
docker run -d --name vroksnet \
  -p 8080:8080 -p 7353:7353 \
  -v vroksnet-data:/app/data \
  ghcr.io/versussun/vroksnet:latest
```

| What | Address |
|---|---|
| API and Admin UI | `http://localhost:8080` |
| Provider port (Type 3 mock) | `http://localhost:7353` |

Data is kept in the `vroksnet-data` volume. Brokers are not part of the image; use your own RabbitMQ / NATS / Kafka.

### API examples in these guides

The `curl` examples use `$API` for the API address and `jq` to pick ids out of responses:

```bash
API=http://localhost:8080          # Docker
# API=https://localhost:7352       # Aspire (add -k to curl if the dev certificate isn't trusted)
```

## Concepts

**Specification.** An imported OpenAPI 3.x or AsyncAPI 3.0 YAML file. VroksNet splits it into **operations**, each with a key:

- OpenAPI: `METHOD /path`, e.g. `GET /pets/{petId}`
- AsyncAPI: `channel-address:action`, e.g. `orders.created:send`

For each operation VroksNet keeps the example (used as the message to send or the mock's answer) and the JSON Schemas it validates against. Re-uploading a spec with the same `info.title` replaces the previous version.

**Connection.** Where a service or broker lives, created on the **Settings** page:

| Service type | Value | Example |
|---|---|---|
| HTTP | base URL (may include a path and query) | `https://orders.internal/api/v1` |
| RabbitMQ | AMQP connection string | `amqp://user:password@rabbit:5672/vhost` |
| NATS | NATS URL | `nats://user:password@nats:4222` |
| MQTT | `mqtt://` URL, or `mqtts://` for TLS (MQTT 5) | `mqtt://user:password@broker:1883` |
| Kafka | bootstrap servers, or librdkafka `key=value;…` settings with `bootstrap.servers` (for SASL/TLS; put a value in double quotes to use `;` or `=` in it), or an Azure Event Hubs connection string | `kafka1:9092,kafka2:9092` or `bootstrap.servers=kafka:9093;security.protocol=SASL_SSL;sasl.mechanism=PLAIN;sasl.username=u;sasl.password=p` or `Endpoint=sb://shop.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…` |

**Azure Event Hubs** is used through its Kafka endpoint, as a **Kafka** connection: paste the namespace's connection string as the value. VroksNet connects to `<namespace>:9093` over SASL_SSL with the connection string as the password — the [standard Event Hubs Kafka settings](https://learn.microsoft.com/azure/event-hubs/azure-event-hubs-apache-kafka-overview#security-and-authentication). An event hub is a topic, so the AsyncAPI channel address must be the event hub's name. For the [Event Hubs emulator](https://learn.microsoft.com/azure/event-hubs/test-locally-with-event-hub-emulator), use its connection string (`…;UseDevelopmentEmulator=true`): VroksNet then uses port 9092 without TLS, so the emulator's Kafka port must be published as 9092. Send, Listen and Test connection all work against the emulator (verified with its default setup). Other settings — OAuth, a different port — go in the `key=value` form, with the password quoted: `sasl.password="Endpoint=sb://…;SharedAccessKeyName=…;SharedAccessKey=…"`.

Use **Test connection** in the form (or the **Test** button in the list) to check it's reachable.

**Test scenario.** A saved "run this operation through this connection", on the **Test Scenarios** page. Types 1, 2 and 4 are scenarios. Each run records whether it passed, and **Last run** in the list shows the latest result.

**Call History.** Every scenario run and every call to the mock, newest first, with the HTTP status and contract result. See [Call History](call-history.md).

## Import a specification

**UI:** **Specifications** → pick **Kind** (OpenAPI or AsyncAPI) → **Upload spec** → choose the YAML file. Click the title to see its operations; **View** shows the raw YAML.

**API:**

```bash
curl -s -X POST "$API/api/specifications/openapi" \
  -H "Content-Type: text/plain" -H "X-File-Name: petstore.yaml" \
  --data-binary @petstore.yaml
# → {"id":"3f2a…"}

curl -s -X POST "$API/api/specifications/asyncapi" \
  -H "Content-Type: text/plain" --data-binary @orders.yaml
```

A file that doesn't parse returns `400` with the parser's message.

## Create a connection

**UI:** **Settings** → **+ Add connection** → Name, Service type, URL / Connection string → **Add**.

**API:**

```bash
curl -s -X POST "$API/api/connections" -H "Content-Type: application/json" \
  -d '{"name":"Orders API","serviceType":"Http","value":"https://orders.internal"}'
# → {"id":"9c1e…"}

curl -s -X POST "$API/api/connections/<connection-id>/test"
# → {"success":true,"message":"Reached orders.internal — responded 200 OK."}
```
