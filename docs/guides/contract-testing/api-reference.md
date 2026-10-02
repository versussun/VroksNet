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
| `POST /api/connections` | `{ "name", "serviceType": "Http" \| "RabbitMq" \| "Nats", "value" }` | `{ "id" }` |
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
  "listenExchange": null
}
```

- `kind`: `"Send"` (HTTP request or broker publish, the default) or `"Listen"` (broker operations only).
- `listenTimeoutSeconds`: 1–80, `null` = 30. Listen only.
- `listenExchange`: RabbitMQ Listen only, `null` = `amq.topic`.
- An operation/connection mismatch (an HTTP operation through a broker connection, etc.) is rejected.

## Mock and provider mode (Type 3)

| Method & path | Body | Returns |
|---|---|---|
| any of GET/POST/PUT/PATCH/DELETE `/mock/{path}` | the request | the matching enabled operation's example, placeholders filled in, at the spec's status; or `404` |
| anything on the provider port (`:7353`), at the real path | the request | the same, for an operation served at its real path; or `404` |
| `PUT /api/mock-endpoints/{id}/provider-mode` | `{ "enabled": true }` | `204`; `404`; `409 { "detail" }` with the reason (e.g. an overlap) |
| `PUT /api/mock-endpoints/{id}/enabled` | `{ "enabled": false }` | `204`; `404`; `409 { "detail" }` for a non-HTTP operation. A disabled operation answers `404` under `/mock` and on the provider port |
| `GET /api/system/provider` | | `{ "enabled", "port", "publicUrl" }` |

## Call History

| Method & path | Query / body | Returns |
|---|---|---|
| `GET /api/call-records` | `specificationId`, `mockEndpointId`, `testScenarioId`, `direction`, `contractValid`, `cursor`, `limit` | `{ "items": [], "nextCursor" }` — each item carries `statusCode`, `contractValid`, `validationErrors[]` and `warnings[]` (e.g. response placeholders that couldn't be filled in) |
| `GET /api/call-records/{id}` | | `{ "id", "requestSnapshot", "responseSnapshot" }` / `404` |
| `DELETE /api/call-records` | | `{ "deleted" }` |
