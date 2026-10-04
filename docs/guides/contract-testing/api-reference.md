# API quick reference

[← Contract testing guides](README.md)

All admin endpoints live on the main address (`$API`, see [Getting started](getting-started.md#api-examples-in-these-guides)). JSON uses camelCase, and enums are strings (`"Http"`, `"Listen"`, …).

## Specifications

| Method & path | Body / query | Returns |
|---|---|---|
| `POST /api/specifications/openapi` | the YAML (`Content-Type: text/plain`); optional `X-File-Name` header | `{ "id" }`, or `400 { "message" }` if it doesn't parse |
| `POST /api/specifications/asyncapi` | same | same |
| `GET /api/specifications` | | list of specifications |
| `GET /api/specifications/{id}` | | the spec with `endpoints[]`: `id`, `operationKey`, `isEnabled`, `serveAtRealPath`, `exampleTemplate`, `requiresHttpConnection`, `canListen`, `defaultTestScenarioKind`; plus `protocols` (AsyncAPI `servers.*.protocol`, lower-case) and `connectionTypes` (the connection types that speak them — the forms offer those first) |
| `PUT /api/specifications/{id}/provider-mode` | `{ "enabled": true }` | `{ "served": [], "skipped": [{ "operationKey", "reason" }], "refusal" }`; `404`; `409` if it couldn't be saved |

Names of connections, test scenarios and Publishers are unique within each kind, and stored without surrounding spaces. Creating or renaming one to a taken name is a `400` with `{ "detail": "A test scenario named \"…\" already exists." }`; other invalid input (a blank name, an operation that can't go through the connection, …) is a `400` with the reason too.

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
| `GET /api/test-scenarios` | | list, with `lastRunAt`, `lastRunSuccess`, `lastRunMessage`, `provisionedAt`, `schedule`, `scheduleTimeZone`, `nextScheduledRunAt` |
| `GET /api/test-scenarios/{id}` | | one scenario |
| `PUT /api/test-scenarios/{id}` | same as create | `204` / `404` |
| `DELETE /api/test-scenarios/{id}` | | `204` / `404` |
| `POST /api/test-scenarios/{id}/run` | | runs it synchronously: `{ "success", "message", "responseBody", "statusCode", "contractValidation": { "isValid", "errors" } \| null }` |
| `POST /api/test-scenarios/{id}/runs` | none, or `{ "runAt" }` / `{ "delaySeconds" }` | runs it in the background: `202 { "runId" }` with `Location: /api/test-runs/{runId}`; `404`. With a body it's a delayed one-off run (`trigger: "Delayed"`, `Queued` until its time, cancellable): `runAt` is a time with an offset, `delaySeconds` 1–2592000; up to 30 days ahead, not both — otherwise `400` with the reason |
| `GET /api/test-scenarios/schedule-preview?schedule=&timeZone=&count=` | | `{ "error", "nextRuns": [UTC times] }` — always `200`; `error` says why the schedule can't be saved. `count` 1–20, default 5 |

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
  "brokerOptions": null,
  "schedule": null,
  "scheduleTimeZone": null
}
```

- `kind`: `"Send"` (HTTP request or broker publish, the default) or `"Listen"` (broker operations only).
- `payloadOverride`: `null` sends the operation's own body — for an HTTP operation its request body (the spec's request example, or one built from the request schema; none if it declares no request body), for a broker operation its message example. Either way it's a template: `{{uuid}}`/`{{now}}` are filled in, `{{request.*}}` becomes a warning.
- `listenTimeoutSeconds`: 1–1800 (30 minutes), `null` = 30. Listen only. Over 80, the scenario can only run in the background: the synchronous `/run` returns `success: false` with a message saying so, without running.
- `brokerOptions`: the connection type's own settings (ADR 0003), an object of strings; `null` or a blank value = the default. `GET /api/system/connection-types` lists what each type accepts (`options`); anything else is a `400`. RabbitMQ has one, `exchange`: Send publishes to it with routing key = channel address, default the default exchange `""` (straight into the queue named after the channel); Listen binds to it, default `amq.topic`. A missing exchange fails the run with a readable message. The other types' options are in [getting-started](getting-started.md): MQTT `qos`/`retain`, Redis `mode`, Azure Service Bus `subscription` (Listen only), SQS/SNS `messageGroupId` (Send only), SNS `queue` (Listen only).
- `exchange` (**deprecated**, removed in contract v2): the same as `brokerOptions.exchange`, still accepted and still returned. It's dropped for a type without that option, as before; set to a different value than `brokerOptions.exchange` it's a `400`.
- A Listen whose channel the connection's broker can't subscribe to is a `400` with the reason — e.g. a `/`-separated channel with parameters through RabbitMQ or NATS, whose wildcards only match whole `.`-separated words.
- `schedule`: a standard 5-field cron expression (minute hour day-of-month month day-of-week, no seconds), e.g. `"0 9 * * 1-5"`; `null` = not scheduled. The background worker queues the next run (`trigger: "Schedule"`), so it shows in `/api/test-runs` as `Queued` until its time. Runs missed while the app was down aren't caught up. Changing or clearing the schedule drops the queued run.
- `scheduleTimeZone`: an IANA name (`"Europe/Kyiv"`) the schedule is read in, with its daylight-saving rules; `null` = UTC. Only with a `schedule`. An invalid expression or unknown zone is a `400` with the reason.
- An operation/connection mismatch (an HTTP operation through a broker connection, etc.) is rejected.

## Test runs

Every run of a scenario, synchronous or in the background, is recorded. A background run is queued and picked up within a second; at most one runs per scenario at a time, and at most four in all. The last 100 finished runs of each scenario are kept.

| Method & path | Query / body | Returns |
|---|---|---|
| `GET /api/test-runs` | `testScenarioId`, `status`, `cursor`, `limit` (default 50, max 200) | `{ "items": [], "nextCursor" }`, newest first; `400` for a bad cursor |
| `GET /api/test-runs/{id}` | | one run, see below; `404` |
| `POST /api/test-runs/{id}/cancel` | | `204` — a queued run is cancelled at once, a running one stops within moments (poll it); `404`; `409` if it has already finished |

A run:

```json
{
  "id": "…",
  "testScenarioId": "…",
  "status": "Passed",
  "trigger": "Manual",
  "scheduledFor": "2026-10-03T10:00:00+00:00",
  "startedAt": "2026-10-03T10:00:00.4+00:00",
  "finishedAt": "2026-10-03T10:00:01.2+00:00",
  "message": "200 OK",
  "statusCode": 200,
  "contractValid": true,
  "validationErrors": []
}
```

- `status`: `Queued` → `Running` → `Passed`, `Failed`, `Cancelled`, or `Interrupted` (the app stopped while it ran).
- `trigger`: `Manual` (someone ran it, from the UI or the API), `Schedule` (the scenario's cron schedule), `Delayed` (a run queued for later), `Suite` (part of a suite run). A suite run's own `trigger` is `Manual` or `Startup`.
- The run's traffic is in the call history: `GET /api/call-records?testRunId={id}`.

## Test suites

A suite is a named list of scenarios run together — the unit a CI pipeline runs. A suite run starts **every Listen first**, and the Sends only once each Listen is subscribed (or has failed, or 30 seconds passed), so a suite can check a whole chain: send a command → the service handles it → it publishes an event → the Listen catches it. Sends then run one after another in the suite's order. The suite passes only if every run passed. `{suite}` is the suite's id or its name.

| Method & path | Body / query | Returns |
|---|---|---|
| `POST /api/test-suites` | `{ "name", "testScenarioIds": ["…"], "runOnStartup": false }` | `{ "id" }`; `400` for a blank or taken name, no scenarios, a repeat or an unknown scenario |
| `GET /api/test-suites` | | `[{ "id", "name", "scenarios": [{ "id", "name", "kind" }], "runOnStartup", "updatedAt", "provisionedAt", "lastRun" }]` |
| `GET /api/test-suites/{suite}` | | one suite; `404` |
| `PUT /api/test-suites/{id}` | same as create | `204` / `404` / `400` |
| `DELETE /api/test-suites/{id}` | | `204` / `404`; its run history stays |
| `POST /api/test-suites/{suite}/runs` | | `202 { "suiteRunId" }` with `Location: /api/suite-runs/{suiteRunId}`; `404` |
| `GET /api/test-suites/{suite}/runs` | `limit` (default 20, max 100) | the suite's runs, newest first; `404` |
| `GET /api/test-suites/{suite}/runs/latest` | | the latest run; `404` if the suite doesn't exist or hasn't run |
| `GET /api/suite-runs/{id}` | | one suite run, see below; `404` |
| `POST /api/suite-runs/{id}/cancel` | | `204` — it stops with its runs; `404`; `409` if it has already finished |

A suite run:

```json
{
  "id": "…",
  "testSuiteId": "…",
  "suiteName": "payments-contract",
  "status": "Failed",
  "trigger": "Manual",
  "scheduledFor": "…", "startedAt": "…", "finishedAt": "…",
  "message": "1 of 2 passed. Failed: order-created-listen.",
  "runs": [
    { "testScenarioId": "…", "scenarioName": "payments-get", "kind": "Send", "testRunId": "…", "status": "Passed", "message": "200 OK" },
    { "testScenarioId": "…", "scenarioName": "order-created-listen", "kind": "Listen", "testRunId": "…", "status": "Failed", "message": "No message on subject \"orders.created\" within 30s." }
  ],
  "failed": ["order-created-listen"]
}
```

- `status` as for a run; `failed` lists the scenarios whose run didn't pass, once the suite run has finished.
- Each scenario's run is also in `/api/test-runs` with `trigger: "Suite"`, and its traffic in the call history.
- A scenario deleted since the suite was saved stays listed as `(deleted scenario)` and fails its run.
- At most one run per suite at a time; a suite run takes one of the four background slots.
- `runOnStartup: true` runs the suite once each time VroksNet starts (`trigger: "Startup"`), after provisioning succeeded — or when there's none. After a failed provisioning (with `Provisioning__FailOnError=false`) it isn't run. It doesn't hold up `/health`.
- **In CI:** `scripts/run-test-suite.sh <url> <suite>` starts the suite, waits, prints each scenario's result and exits `0` when it passed, `1` when it didn't, `2` when it couldn't run (see the runbook).

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
| `GET /api/publishers` | | `[{ "id", "name", "operationKey", "connectionName", "brokerOptions", "exchange", "intervalSeconds", "isEnabled", "lastPublishedAt", "lastPublishSuccess", "lastPublishMessage", "provisionedAt", … }]` |
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
  "brokerOptions": { "exchange": "amq.topic" },
  "enabled": true
}
```

- The operation must be an AsyncAPI one and the connection a RabbitMQ/NATS/Kafka one. `intervalSeconds` is 1–86400.
- `payloadOverride`: `null` publishes the operation's own example. Either way it's a template: `{{uuid}}` and `{{now}}` are filled in per message. `{{request.*}}` has no request behind it, so it becomes `null`/empty and shows up as a warning.
- `brokerOptions`: as for Test Scenarios. RabbitMQ's `exchange` defaults to the default exchange (straight into the queue named after the channel). The deprecated `exchange` field still works as there.
- Each publish is checked against the operation's payload schema. A mismatch is reported in `message`/`contractValidation` and in Call History, but the message is still sent.

## Call History

| Method & path | Query / body | Returns |
|---|---|---|
| `GET /api/call-records` | `specificationId`, `mockEndpointId`, `testScenarioId`, `publisherId`, `testRunId`, `direction`, `contractValid`, `cursor`, `limit` | `{ "items": [], "nextCursor" }` — each item carries `statusCode`, `contractValid`, `validationErrors[]`, `warnings[]` (e.g. placeholders that couldn't be filled in) and `testScenarioName`/`publisherName` |
| `GET /api/call-records/{id}` | | `{ "id", "requestSnapshot", "responseSnapshot" }` / `404` |
| `DELETE /api/call-records` | | `{ "deleted" }` |
