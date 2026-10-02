# Type 4: Provider test (listen for a message and validate it)

[← Contract testing guides](README.md)

**Checks:** that a service publishes messages matching its AsyncAPI spec.
**How:** a run subscribes to the operation's channel, waits for the next message, and validates its payload against the spec's payload schema. It uses its own temporary subscription, so the channel's real consumers keep getting every message.

```
your service ── publish ──▶ RabbitMQ / NATS / Kafka ──▶ VroksNet (temporary subscription)
                                              payload checked against the spec
```

## 1. The spec

An AsyncAPI 3.0 operation with a payload schema. `action: send` means "the service sends this", which is what you listen for:

```yaml
asyncapi: 3.0.0
info:
  title: Orders Events
  version: "1.0.0"
channels:
  orderCreated:
    address: orders.created
    messages:
      orderCreated:
        $ref: "#/components/messages/OrderCreated"
operations:
  publishOrderCreated:
    action: send
    channel:
      $ref: "#/channels/orderCreated"
    messages:
      - $ref: "#/channels/orderCreated/messages/orderCreated"
components:
  messages:
    OrderCreated:
      payload:
        $ref: "#/components/schemas/OrderCreated"
  schemas:
    OrderCreated:
      type: object
      required: [orderId, amount]
      properties:
        orderId: { type: string }
        amount: { type: number }
```

## 2. How VroksNet subscribes

| Broker | Subscription |
|---|---|
| RabbitMQ | a temporary exclusive queue bound to an **exchange** (default `amq.topic`) with binding key = channel address. The service must publish **to that exchange** with routing key `orders.created`. A message sent straight to a queue through the default exchange can't be observed. |
| NATS | a core subscription on subject `orders.created`. |
| Kafka | every partition of topic `orders.created`, read from its current end without a consumer group, so the service's own consumer groups aren't affected. The topic must already exist; a channel parameter (`orders.{region}`) matches every existing topic with any value in that segment. |

**Channel parameters.** An address segment that is a whole AsyncAPI parameter becomes a wildcard: `orders.{region}.created` listens on `orders.*.created`, which matches `orders.eu.created`, `orders.us.created`, and so on. A parameter that is only part of a segment, or an address separated by `/` (`user/{id}/signedup`), can't be subscribed to, and such a scenario can't be saved.

## 3. Create the scenario

Import the spec as **AsyncAPI** and add a **RabbitMQ**, **NATS** or **Kafka** connection ([Getting started](getting-started.md)).

**UI:** **Test Scenarios** → **+ Add test scenario**:

| Field | Value |
|---|---|
| Name | `Order created is valid` |
| Specification | `Orders Events (AsyncApi)` |
| Operation | `orders.created:send` |
| Connection | your RabbitMQ, NATS or Kafka connection |
| Mode | **Listen for a message and validate it** (pre-selected for `send` operations) |
| Wait up to (seconds) | 1–80; blank = 30 |
| Exchange | RabbitMQ only; blank = `amq.topic` |

The scenario row gets a **Listen** badge.

**API:**

```bash
SCENARIO=$(curl -s -X POST "$API/api/test-scenarios" -H "Content-Type: application/json" -d "{
  \"name\": \"Order created is valid\",
  \"specificationId\": \"$SPEC\",
  \"mockEndpointId\": \"$OP\",
  \"connectionId\": \"$CONN\",
  \"payloadOverride\": null,
  \"kind\": \"Listen\",
  \"listenTimeoutSeconds\": 30,
  \"exchange\": \"orders\"
}" | jq -r .id)
```

## 4. Run it, then make the service publish

A run **waits** for the next message, so start it first, then trigger the service (place an order, replay an event, etc.) within the timeout.

**UI:** **Run** shows *Listening…* until a message arrives or the time runs out. **Stop** cancels the wait.

**API:**

```bash
curl -s -X POST "$API/api/test-scenarios/$SCENARIO/run"     # blocks until a message or the timeout
```

Message received and valid:

```json
{
  "success": true,
  "message": "Received on exchange \"orders\", routing key matching \"orders.created\".",
  "responseBody": "{\"orderId\":\"ord_1\",\"amount\":42.5}",
  "statusCode": null,
  "contractValidation": { "isValid": true, "errors": [] }
}
```

Received, but not as the spec says:

```json
{
  "success": false,
  "message": "Received on exchange \"orders\", routing key matching \"orders.created\". — message doesn't match the spec (1 violation(s)).",
  "responseBody": "{\"orderId\":42}",
  "contractValidation": { "isValid": false, "errors": ["Required properties [\"amount\"] are not present"] }
}
```

Nothing arrived:

```json
{ "success": false, "message": "No message on exchange \"orders\", routing key \"orders.created\" within 30s.", "contractValidation": null }
```

Other failures name the stage: `Timed out connecting to the broker…`, `Connected, but setting up the subscription … timed out…`, or `Exchange "orders" doesn't exist on this broker.`

## Good to know

- **Default mode.** For `send` operations the form starts in Listen mode. Switch **Mode** to publish instead ([Type 2](type-2-producer-publish.md)).
- **No schema in the spec** means any message passes (`contractValidation: null`). An empty message where a schema is declared is a violation.
- **A Listen scenario hears a Type 2 publish from VroksNet** on the same RabbitMQ operation as long as both use the same exchange (both start as `amq.topic`). A Send scenario whose Exchange is cleared publishes through the default exchange, which can't be listened to.
- Received messages are logged in [Call History](call-history.md) as *Broker message (in)*, with the payload.
