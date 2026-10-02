# Type 3: Provider test (HTTP mock that checks incoming requests)

[← Contract testing guides](README.md)

**Checks:** that a service sends correct HTTP requests to a dependency it calls.
**How:** you point the service at VroksNet instead of the real dependency. VroksNet answers at the dependency's **real paths** with the spec's example and checks each incoming request body against the spec. Unlike the other types, this isn't a scenario you run: it's a mode you switch on, and every call the service makes is checked as it happens.

```
your service ──── POST /orders {…} ────▶ VroksNet provider port (7353)
             ◀─── 200 (spec's example) ─  request body checked against the spec, logged
```

## 1. The spec (of the dependency)

Import the OpenAPI spec of the API your service **calls**. Request bodies need a JSON schema to be checkable:

```yaml
openapi: 3.0.3
info:
  title: Payments API
  version: "1.0.0"
paths:
  /payments:
    post:
      requestBody:
        content:
          application/json:
            schema:
              type: object
              required: [orderId, amount]
              properties:
                orderId: { type: string }
                amount: { type: number, minimum: 0 }
      responses:
        "200":
          description: Accepted
          content:
            application/json:
              example:
                paymentId: "pay_1"
                status: "accepted"
  /payments/{paymentId}:
    get:
      responses:
        "200":
          description: The payment
          content:
            application/json:
              example:
                paymentId: "pay_1"
                status: "settled"
```

## 2. Serve the operations at their real paths

**UI:** open the specification (**Specifications** → its title):

- **Serve at real path** switch on an operation card turns it on for that operation. The help text under it shows the exact address, e.g. `POST http://localhost:7353/payments`.
- **Serve all at real paths** turns it on for every HTTP operation of the spec; **Stop serving at real paths** turns them all off.

**API:**

```bash
# one operation
curl -s -X PUT "$API/api/mock-endpoints/$OP/provider-mode" \
  -H "Content-Type: application/json" -d '{"enabled": true}'
# → 204 No Content

# the whole specification
curl -s -X PUT "$API/api/specifications/$SPEC/provider-mode" \
  -H "Content-Type: application/json" -d '{"enabled": true}'
# → {"served":["GET /payments/{paymentId}","POST /payments"],"skipped":[],"refusal":null}
```

## 3. Point your service at the provider port

Change the service's base URL for the dependency to the provider port:

| VroksNet runs… | Provider port |
|---|---|
| under Aspire (`dotnet run --project src/VroksNet.AppHost`) | `http://localhost:7353` |
| in Docker (`-p 7353:7353`) | `http://<docker-host>:7353` |

`GET $API/api/system/provider` tells you whether the port is on and its public address (`publicUrl` is `null` behind Docker's port mapping, since VroksNet can't know the host-side address).

The provider port serves **only** the mock: `/api`, `/health` and the Admin UI aren't reachable there, so the dependency's paths can be anything.

Try it by hand:

```bash
curl -s -X POST http://localhost:7353/payments \
  -H "Content-Type: application/json" -d '{"orderId":"ord_1","amount":-5}'
# → {"paymentId":"pay_1","status":"accepted"}   (answered anyway; the violation is logged)
```

## 4. Check the results in Call History

Every call shows up in [Call History](call-history.md) as *Mock call (in)*, with its real path (`POST /payments`, no `/mock` prefix) and a contract badge:

- **Matches spec:** the body matched the request schema.
- **Contract violated:** it didn't. **Details** lists why, e.g. `-5 should be at least 0` for the `amount` above.
- **—:** not checked (see below).

The mock's answer doesn't depend on the result: your service always gets the example, so its own flow keeps going.

```bash
curl -s "$API/api/call-records?specificationId=$SPEC&direction=InboundHttpRequest&contractValid=false" \
  | jq '.items[] | {requestLine, validationErrors}'
```

## What is and isn't checked

| Incoming request | Checked? |
|---|---|
| `Content-Type: application/json` or `…+json`, body present | ✅ against the spec's request schema |
| no body | — (the spec doesn't say whether the body is required) |
| other content types (form data, text) | — (only the JSON schema is stored) |
| body over 64K characters | — (it's stored truncated) |
| path/method not served at real path | answered `404` with a hint, logged |

## Rules and limits

- **No overlapping operations.** Two served operations must never be able to match the same request: `GET /pets/{id}` and `GET /pets/mine` overlap, and so does the same path in two specs. Turning on an overlapping one is refused, and the switch shows which operation and spec it collides with. **Serve all** serves what it can and lists the rest as *skipped*.
- **Answers are static:** the spec's example with status `200`. Dynamic responses (`{{request.path.id}}`, the spec's status codes) are planned as Phase F.
- `HEAD` is answered like `GET`.
- **No CORS** on the provider port: a browser front end pointed at it fails its preflight. A configurable CORS policy is planned as Phase H.
- Re-importing the spec keeps the switch on for operations whose method and path didn't change.
- The `/mock/<path>` prefix on the main API address answers **every** enabled operation (no switch needed) and checks request bodies the same way. It's handy for quick manual tries; the provider port is meant for pointing real services at.
