# Sample specifications

Ready-to-import specs for trying VroksNet locally. Import them on the **Specifications** page,
or over the API:

```bash
# OpenAPI / Swagger
curl -k -X POST https://localhost:7352/api/specifications/openapi \
  -H "Content-Type: text/plain" -H "X-File-Name: bookstore-openapi.yaml" \
  --data-binary @docs/samples/bookstore-openapi.yaml

# AsyncAPI
curl -k -X POST https://localhost:7352/api/specifications/asyncapi \
  -H "Content-Type: text/plain" -H "X-File-Name: shop-events-kafka-asyncapi.yaml" \
  --data-binary @docs/samples/shop-events-kafka-asyncapi.yaml
```

To start a container with some of them already imported and wired up, see
[`provisioning/`](provisioning/README.md).

`SampleSpecificationsTests` (unit tests) imports every file here and checks that each
operation's example passes its own schema. If you edit a sample, run the unit tests.

## What's here

| File | Kind | Shows |
|---|---|---|
| [petstore-openapi.yaml](petstore-openapi.yaml) | OpenAPI 3.0 | The minimum: 3 operations, static examples, `allOf`. |
| [bookstore-openapi.yaml](bookstore-openapi.yaml) | OpenAPI 3.0 | Extended REST mock: path/query/header params, shared `components.parameters`/`responses`, every templating placeholder, 201/202/204 answers, error schemas (404/409/422/`default`) for contract tests. |
| [payments-openapi-3.1.yaml](payments-openapi-3.1.yaml) | OpenAPI 3.1 | `type: [string, "null"]`, `const`, `oneOf` + `discriminator`, security schemes, reusable headers, `webhooks`. |
| [inventory-swagger-2.0.yaml](inventory-swagger-2.0.yaml) | Swagger 2.0 | A legacy spec: `definitions`, `in: body`, `examples: application/json`. Upgraded to the 3.x model on import. |
| [orders-asyncapi.yaml](orders-asyncapi.yaml) | AsyncAPI 3.0 | The minimum AsyncAPI that still yields an example and a schema. Any broker. |
| [shop-events-kafka-asyncapi.yaml](shop-events-kafka-asyncapi.yaml) | AsyncAPI 3.0 | Kafka: 5 operations (send + receive), a `{region}` channel parameter, Kafka bindings, rich nested payloads, several examples per message. |
| [iot-telemetry-nats-asyncapi.yaml](iot-telemetry-nats-asyncapi.yaml) | AsyncAPI 3.0 | NATS: `devices.{deviceId}.telemetry` subjects, a simulated device to publish as, a message declared inline in its channel. |
| [home-sensors-mqtt-asyncapi.yaml](home-sensors-mqtt-asyncapi.yaml) | AsyncAPI 3.0 | MQTT: `/`-separated topics, a `home/{room}/temperature` listen pattern (`+`), a simulated sensor to publish as. |
| [chat-rooms-redis-asyncapi.yaml](chat-rooms-redis-asyncapi.yaml) | AsyncAPI 3.0 | Redis: a Pub/Sub `chat.{room}.message` listen pattern, a simulated user to publish as, and an `audit.logins` stream (broker option `mode=stream`). |
| [warehouse-servicebus-asyncapi.yaml](warehouse-servicebus-asyncapi.yaml) | AsyncAPI 3.0 | Azure Service Bus: a queue (Send only) and a topic listened on through the emulator's `vroksnet` subscription. |
| [notifications-rabbitmq-asyncapi.yaml](notifications-rabbitmq-asyncapi.yaml) | AsyncAPI 3.0 | RabbitMQ: a topic exchange, routing keys, AMQP bindings, a `notify.{channel}.delivered` listen pattern. |

## Trying them out

**REST mocks.** After importing `bookstore-openapi.yaml`:

```bash
curl -k "https://localhost:7352/mock/books?q=ddd"
curl -k -X POST https://localhost:7352/mock/books -H "X-Request-Id: r-1" \
  -H "Content-Type: application/json" \
  -d '{"isbn":"978-1","title":"Refactoring","authors":["Fowler"],"price":{"amount":45,"currency":"EUR"}}'
# → 201, with a new id, your isbn/title and the X-Request-Id echoed back
```

**Async mocks.** Add a connection on **Settings** (the dev brokers' connection strings are in the
Aspire dashboard: `rabbitmq`, `nats`, `kafka`; MQTT is `mqtt://localhost:<port>` of the `mqtt` resource; Redis and Service Bus are the `redis` and `servicebus` resources' connection strings), then:

| Sample | Publisher / Send scenario | Listen scenario |
|---|---|---|
| Kafka | `shop.orders.created:send` every 5s | `shop.payments.{region}.settled:send` hears every region's topic (the topics must exist) |
| NATS | `devices.sim-001.telemetry:send` simulates a device | `devices.{deviceId}.telemetry:send` hears all devices |
| MQTT | `home/kitchen/temperature:send` simulates a sensor | `home/{room}/temperature:send` hears every room (`home/+/temperature`) |
| Redis | `chat.lobby.message:send` simulates a user; `audit.logins:send` with `mode=stream` appends a login | `chat.{room}.message:send` hears every room (`chat.*.message`); `audit.logins:send` with `mode=stream` waits for the next login |
| Service Bus | `warehouse.picking.requested:receive` sends a picking request to the queue | `warehouse.stock.changed:send` with `subscription=vroksnet` waits for the next stock change (a queue can't be listened on) |
| RabbitMQ | `notify.email.requested:receive` with exchange `notifications` | `notify.{channel}.delivered:send` on exchange `notifications` |

A Listen and a Send scenario on the same broker meet, so you can check a whole round trip
without a service under test. For RabbitMQ the exchange must exist, e.g. created in the
management UI.

## Import rules worth knowing

These apply to your own specs too.

**Both kinds**
- A spec is matched by `info.title`. Importing a file with the same title replaces the earlier
  import. There's no editing of examples in the UI, so change the spec and re-import it.
- Only **one example per operation** is used:
  - OpenAPI: the `example` of the lowest-status response with an `application/json` body, otherwise
    the request body's `example`. Named `examples:` maps aren't read; use a single `example`.
  - AsyncAPI: the first `examples[].payload` of the operation's first message.
- Schemas are stored at import time for contract checks. A status (OpenAPI) or message (AsyncAPI)
  with no JSON schema can't be validated.

**Templating** (`{{request.path.x}}`, `{{request.query.x}}`, `{{request.header.X-Name}}`,
`{{request.body.a.b}}` / `{{request.body.$.a.b}}`, `{{request.body}}`, `{{uuid}}`, `{{now}}`)
- **Quote every placeholder in YAML** (`id: "{{uuid}}"`). Unquoted, `{{` starts a YAML flow mapping
  and the import fails or produces garbage.
- Since it's quoted, a placeholder always lands **inside a JSON string**. Echoing a number, array
  or object from the request therefore gives its JSON *text* as a string, which won't match a
  `type: integer`/`array`/`object` schema. That's why the samples only echo string fields.
  Mock responses currently have no way to echo a non-string value as-is. (Send scenarios can: a
  scenario's payload override is raw JSON.)
- Don't put a placeholder in a field restricted by `enum`, `pattern` or `const`. The raw example
  would then fail its own schema.
- AsyncAPI messages only have `{{uuid}}` and `{{now}}`, since there's no request to read from.
  They're filled in on every publish.

**AsyncAPI specifics**
- Only the **AsyncAPI 3.0** layout is read: top-level `operations` with `action`,
  `channel.$ref` and `messages: [ $ref ]`. An AsyncAPI 2.x file (`publish`/`subscribe` under
  `channels`) imports with no operations, so convert it first.
- The operation key is `"{channel address}:{action}"`. The address is the RabbitMQ routing key,
  the NATS subject or the Kafka topic. The exchange comes from the Publisher/scenario, never from
  `bindings`; bindings and security are documentation only. `servers` only say what kind of
  connection fits: their `protocol` (`kafka`, `amqp`, `nats`, …) puts matching connections first in
  the Test Scenario and Publisher forms. The host and credentials still come from the connection.
- A message may be declared inline in the channel or `$ref` `components.messages`. Its `payload`
  may be inline or one `$ref` to `components.schemas`. **The schema that ref points to must be
  self-contained**: a `$ref` nested inside it isn't resolved, and contract checks against it
  report "couldn't be evaluated".
- A channel parameter has to be a whole segment, between `.`s (`orders.{region}.created`) or `/`s
  (`user/{id}/signedup`). Only then can a Listen scenario subscribe to it: `orders.*.created` on
  RabbitMQ and NATS, a topic regex on Kafka. RabbitMQ and NATS wildcards only stand for whole
  `.`-separated words, so a `/`-separated channel with parameters can be listened on through Kafka
  only.
  Publishers and Send scenarios don't fill parameters in. They would publish to the literal
  `{region}` address, so give them an operation with a concrete address, as the samples do.
