# Call History

[← Contract testing guides](README.md)

Every test scenario run and every call to the mock is recorded, newest first, with its HTTP status and the result of the contract check.

| Kind (as shown) | Comes from | Contract check |
|---|---|---|
| HTTP request (out) | a [Type 1](type-1-consumer-http.md) run | the service's response |
| Broker publish (out) | a [Type 2](type-2-producer-publish.md) run | none |
| Mock call (in) | a call to `/mock/…` or the provider port ([Type 3](type-3-provider-http.md)) | the incoming request body |
| Broker message (in) | a [Type 4](type-4-provider-listen.md) run | the received message |

## In the Admin UI

**Call History** in the menu:

- **Filters:** *Specification*, *Kind*, *Contract* (Matches spec / Contract violated).
- **Columns:** time, kind, the operation (or, for a mock call that matched nothing, the request line that was received), the scenario and connection, HTTP status, contract badge (`—` = not checked).
- **Details** opens the row: the request line, the list of contract violations, and the request and response bodies. Bodies are loaded on demand and capped at 64K characters (longer ones end with `…(truncated)`).
- **Load older** pages back, **Refresh** reloads, and **Clear history** deletes everything (after a confirmation).

## Through the API

```bash
# newest 50 calls
curl -s "$API/api/call-records"

# only contract violations of one specification
curl -s "$API/api/call-records?specificationId=$SPEC&contractValid=false"

# the runs of one scenario
curl -s "$API/api/call-records?testScenarioId=$SCENARIO"

# a kind: InboundHttpRequest | OutboundHttpRequest | OutboundBrokerPublish | InboundBrokerMessage
curl -s "$API/api/call-records?direction=InboundHttpRequest&limit=200"
```

```json
{
  "items": [
    {
      "id": "5b0c…",
      "timestamp": "2026-10-02T09:12:44.512+00:00",
      "direction": "InboundHttpRequest",
      "specificationTitle": "Payments API",
      "operationKey": "POST /payments",
      "requestLine": "POST /payments",
      "statusCode": 200,
      "contractValid": false,
      "validationErrors": ["-5 should be at least 0"],
      "connectionName": null,
      "testScenarioName": null
    }
  ],
  "nextCursor": "639…-5b0c…"
}
```

Paging: pass `nextCursor` back as `cursor` to get the next (older) page; it's `null` on the last page. `limit` is 50 by default, at most 200.

Bodies of one record:

```bash
curl -s "$API/api/call-records/<record-id>"
# → {"id":"…","requestSnapshot":"{\"orderId\":\"ord_1\",\"amount\":-5}","responseSnapshot":"{\"paymentId\":\"pay_1\",…}"}
```

Clear everything:

```bash
curl -s -X DELETE "$API/api/call-records"
# → {"deleted":1234}
```

The history has no automatic retention, so clear it from time to time if the mock gets a lot of traffic.
