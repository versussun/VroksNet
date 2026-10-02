# Type 2: Producer test (publish a message)

[← Contract testing guides](README.md)

**Checks:** that a service which consumes messages handles the ones its AsyncAPI spec describes. VroksNet publishes the operation's example (or your own payload) to the broker; what the service then does with it is up to your own assertions (its logs, its database, a follow-up Type 1 or Type 4 test).
**How:** fire-and-forget. A run passes when the broker accepts the message; nothing is validated.

```
VroksNet ── publish {"orderId":"ord_1",…} ──▶ RabbitMQ / NATS / Kafka ──▶ your service
```

## 1. The spec

An AsyncAPI 3.0 operation with an example payload. `action: receive` means "the service receives this", which is what you publish to:

```yaml
asyncapi: 3.0.0
info:
  title: Shipping Events
  version: "1.0.0"
channels:
  orderShipped:
    address: orders.shipped
    messages:
      orderShipped:
        $ref: "#/components/messages/OrderShipped"
operations:
  consumeOrderShipped:
    action: receive
    channel:
      $ref: "#/channels/orderShipped"
    messages:
      - $ref: "#/channels/orderShipped/messages/orderShipped"
components:
  messages:
    OrderShipped:
      payload:
        type: object
        required: [orderId, trackingNumber]
        properties:
          orderId: { type: string }
          trackingNumber: { type: string }
      examples:
        - name: Shipped
          payload:
            orderId: "ord_1"
            trackingNumber: "1Z999AA10123456784"
```

The operation key is `orders.shipped:receive`, so the channel address is `orders.shipped`.

## 2. Where the message goes

| Broker | Published to |
|---|---|
| RabbitMQ | the scenario's **Exchange** with routing key = channel address (`orders.shipped`). A new scenario starts with `amq.topic`, so the service's queue must be bound to it with a matching key, and a Type 4 Listen scenario on the same exchange hears the message. Clear the field (`exchange: null`) to publish through the **default exchange** `""`, i.e. **straight into the queue named `orders.shipped`**, which must exist. If the exchange doesn't exist, the run fails with a readable message. |
| NATS | subject = channel address (`orders.shipped`). Core NATS doesn't queue: the service must be subscribed when you run. |
| Kafka | topic = channel address (`orders.shipped`). The run succeeds only once the broker has acknowledged the write; a missing topic fails it unless the broker auto-creates topics. |

> If nothing is bound to the exchange with a matching key, RabbitMQ drops the message and the run still succeeds — publishing doesn't confirm delivery.

## 3. Create the scenario

Import the spec as **AsyncAPI** and add a **RabbitMQ**, **NATS** or **Kafka** connection ([Getting started](getting-started.md)).

**UI:** **Test Scenarios** → **+ Add test scenario**:

| Field | Value |
|---|---|
| Name | `Publish order shipped` |
| Specification | `Shipping Events (AsyncApi)` |
| Operation | `orders.shipped:receive` |
| Connection | your RabbitMQ, NATS or Kafka connection |
| Mode | **Publish a message to the channel** (the default for `receive` operations) |
| Payload (optional override) | blank = the spec's example; or your own JSON, e.g. an edge case |
| Exchange | RabbitMQ only; starts as `amq.topic`, blank = straight into the queue named after the channel |

**API:**

```bash
SCENARIO=$(curl -s -X POST "$API/api/test-scenarios" -H "Content-Type: application/json" -d "{
  \"name\": \"Publish order shipped\",
  \"specificationId\": \"$SPEC\",
  \"mockEndpointId\": \"$OP\",
  \"connectionId\": \"$CONN\",
  \"payloadOverride\": \"{\\\"orderId\\\":\\\"ord_42\\\",\\\"trackingNumber\\\":\\\"X1\\\"}\",
  \"kind\": \"Send\",
  \"exchange\": \"amq.topic\"
}" | jq -r .id)
```

`exchange` is RabbitMQ only; leave it out (`null`) to publish straight into the queue through the default exchange.

## 4. Run it

```bash
curl -s -X POST "$API/api/test-scenarios/$SCENARIO/run"
```

```json
{
  "success": true,
  "message": "Published to routing key \"orders.shipped\".",
  "responseBody": null,
  "statusCode": null,
  "contractValidation": null
}
```

`success: false` means the broker couldn't be reached or refused the publish; the message says why.

## Good to know

- To check what the service **sends back** after consuming the message, follow up with a [Type 4](type-4-provider-listen.md) scenario on the channel it publishes to.
- Runs are logged in [Call History](call-history.md) as *Broker publish (out)*, with the payload that was sent.
