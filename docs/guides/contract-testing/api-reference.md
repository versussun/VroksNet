# API quick reference

[← Contract testing guides](README.md)

All admin endpoints live on the main address (`$API`, see [Getting started](getting-started.md#api-examples-in-these-guides)). JSON uses camelCase, and enums are strings (`"Http"`, `"Listen"`, …).

## Specifications

| Method & path | Body / query | Returns |
|---|---|---|
| `POST /api/specifications/openapi` | the YAML (`Content-Type: text/plain`); optional `X-File-Name` header | `{ "id" }`, or `400 { "message" }` if it doesn't parse |
| `POST /api/specifications/asyncapi` | same | same |
| `GET /api/specifications` | | list of specifications |
| `GET /api/specifications/{id}` | | the spec with `endpoints[]`: `id`, `operationKey`, `isEnabled`, `serveAtRealPath`, `exampleTemplate`, `requiresHttpConnection`, `canListen`, `defaultTestScenarioKind` |
| `PUT /api/specifications/{id}/provider-mode` | `{ "enabled": true }` | `{ "served": [], "skipped": [{ "operationKey", "reason" }], "refusal" }`; `404`; `409` if it couldn't be saved |

## Connections

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/connections` | `{ "name", "serviceType": "Http" \| "RabbitMq" \| "Nats" \| "Kafka", "value" }` | `{ "id" }` |
| `GET /api/connections` | | list |
| `PUT /api/connections/{id}` | same as create | `204` / `404` |
| `DELETE /api/connections/{id}` | | `204` / `404` |
| `POST /api/connections/{id}/test` | | `{ "success", "message" }` / `404` |

## Test scenarios (Types 1, 2, 4)

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/test-scenarios` | see below | `{ "id" }` |
| `GET /api/test-scenarios` | | list, with `lastRunAt`, `lastRunSuccess`, `lastRunMessage` |
| `GET /api/test-scenarios/{id}` | | one scenario |
| `PUT /api/test-scenarios/{id}` | same as create | `204` / `404` |
| `DELETE /api/test-scenarios/{id}` | | `204` / `404` |
| `POST /api/test-scenarios/{id}/run` | | `{ "success", "message", "responseBody", "statusCode", "contractValidation": { "isValid", "errors" } \| null }` |

Create/update body:

```json
{
  "name": "Order created is valid",
  "specificationId": "…",
  "mockEndpointId": "…",
  "connectionId": "…",
  "payloadOverride": null,
  "kind": "Send",
  "listenTimeoutSeconds": null,
  "exchange": null
}
```

- `kind`: `"Send"` (HTTP request or broker publish, the default) or `"Listen"` (broker operations only).
- `listenTimeoutSeconds`: 1–80, `null` = 30. Listen only.
- `exchange`: RabbitMQ only (dropped for NATS/HTTP). Send publishes to it with routing key = channel address, `null` = the default exchange `""` (straight into the queue named after the channel); Listen binds to it, `null` = `amq.topic`. A missing exchange fails the run with a readable message. (Was `listenExchange`, Listen only.)
- An operation/connection mismatch (an HTTP operation through a broker connection, etc.) is rejected.

## Mock and provider mode (Type 3)

| Method & path | Body | Returns |
|---|---|---|
| any of GET/POST/PUT/PATCH/DELETE `/mock/{path}` | the request | the matching enabled operation's example, placeholders filled in, at the spec's status; or `404` |
| anything on the provider port (`:7353`), at the real path | the request | the same, for an operation served at its real path; or `404` |
| `PUT /api/mock-endpoints/{id}/provider-mode` | `{ "enabled": true }` | `204`; `404`; `409 { "detail" }` with the reason (e.g. an overlap) |
| `PUT /api/mock-endpoints/{id}/enabled` | `{ "enabled": false }` | `204`; `404`; `409 { "detail" }` for a non-HTTP operation. A disabled operation answers `404` under `/mock` and on the provider port |
| `GET /api/system/provider` | | `{ "enabled", "port", "publicUrl", "corsOrigins" }` — `corsOrigins` is empty when CORS is off on the provider port |

## Publishers (async mocks)

Not a contract test: a publisher publishes an AsyncAPI operation's message to a broker on a schedule, so services that consume it have something to receive.

| Method & path | Body | Returns |
|---|---|---|
| `GET /api/publishers` | | `[{ "id", "name", "operationKey", "connectionName", "exchange", "intervalSeconds", "isEnabled", "lastPublishedAt", "lastPublishSuccess", "lastPublishMessage", … }]` |
| `POST /api/publishers` | see below | `{ "id" }`; `400 { "detail" }` with the reason |
| `PUT /api/publishers/{id}` | same, without `enabled` | `204`; `400`; `404` |
| `PUT /api/publishers/{id}/enabled` | `{ "enabled": true }` | `204`; `404` — start/stop the schedule |
| `POST /api/publishers/{id}/publish` | | `{ "success", "message", "payload", "contractValidation" }` / `404` — publish once now |
| `DELETE /api/publishers/{id}` | | `204` / `404` |

```json
{
  "name": "Order created every 5s",
  "specificationId": "…",
  "mockEndpointId": "…",
  "connectionId": "…",
  "payloadOverride": "{\"orderId\":\"{{uuid}}\",\"at\":\"{{now}}\"}",
  "intervalSeconds": 5,
  "exchange": "amq.topic",
  "enabled": true
}
```

- The operation must be an AsyncAPI one and the connection a RabbitMQ/NATS/Kafka one. `intervalSeconds` is 1–86400.
- `payloadOverride`: `null` publishes the operation's own example. Either way it's a template: `{{uuid}}` and `{{now}}` are filled in per message. `{{request.*}}` has no request behind it, so it becomes `null`/empty and shows up as a warning.
- `exchange`: RabbitMQ only, `null` = the default exchange (straight into the queue named after the channel).
- Each publish is checked against the operation's payload schema. A mismatch is reported in `message`/`contractValidation` and in Call History, but the message is still sent.

## Call History

| Method & path | Query / body | Returns |
|---|---|---|
| `GET /api/call-records` | `specificationId`, `mockEndpointId`, `testScenarioId`, `publisherId`, `direction`, `contractValid`, `cursor`, `limit` | `{ "items": [], "nextCursor" }` — each item carries `statusCode`, `contractValid`, `validationErrors[]`, `warnings[]` (e.g. placeholders that couldn't be filled in) and `testScenarioName`/`publisherName` |
| `GET /api/call-records/{id}` | | `{ "id", "requestSnapshot", "responseSnapshot" }` / `404` |
| `DELETE /api/call-records` | | `{ "deleted" }` |
