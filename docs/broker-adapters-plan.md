# Plan: broker adapters, then new brokers

**Status:** accepted — decisions in ADR 0003 (`docs/adr/0003-broker-adapters.md`). Steps R0–R6 are the refactoring; N1–N5 add brokers on top of it. **Progress:** R0–R6 done (R6: the mechanism; its CI matrix job is uncommented with N2, since GitHub rejects an empty matrix).
**Why:** VroksNet speaks RabbitMQ, NATS and Kafka, and more are wanted (MQTT, Redis, Azure Service Bus, AWS SQS/SNS). Today each broker is a branch in several places, so every new one would touch all of them.

## Where broker-specific code lives today

| Place | What branches on the broker |
|---|---|
| `Infrastructure/Connections/MessageSender.cs` | `switch` on `ConnectionServiceType`: HTTP request, RabbitMQ/NATS/Kafka publish |
| `Infrastructure/Connections/MessageListener.cs` | `switch`: one Listen implementation per broker, each with its own stages and `onListening` point |
| `Infrastructure/Connections/ConnectionTester.cs` | `switch`: one reachability check per type |
| `Infrastructure/Connections/KafkaClients.cs`, `RabbitMqConnections.cs` | client setup shared by the three classes above |
| `Domain/TestScenarios/OperationCompatibility.cs` | "HTTP operation ⇔ `Http` connection, AsyncAPI operation ⇔ any broker" |
| `Domain/TestScenarios/TestScenarioListening.cs` (before R3) | one subscription pattern (`.`-separated segments, `*` for a parameter) shared by all brokers; Kafka turns it into a regex in `KafkaClients.TopicRegexOf` |
| `TestScenario.Exchange`, `Publisher.Exchange` | a RabbitMQ-only field carried through the entities, DTOs, endpoints, Web forms, the manifest (`exchange`) and the export |
| `Web/Components/Pages/Settings.razor` | the list of types and each type's value hint, written out by hand |
| `docs/schemas/provisioning-manifest.v1.schema.json` | the `type` enum |

A new broker therefore means edits in about ten files across four projects, and each class above keeps growing.

## Target design

- **One adapter per broker** in Infrastructure, behind the interfaces Application already has. Each adapter covers:
  - the connection check;
  - Send;
  - Listen (with its `onListening` point);
  - its subscription syntax;
  - validation of its own options.
  `IMessageSender`, `IMessageListener` and `IConnectionTester` stay as they are and become thin dispatchers, so Application and the callers don't change.
- **What each type can do is data, in Domain:** a small descriptor per `ConnectionServiceType` — HTTP or broker, can it Listen, why not. Domain rules and the UI read it instead of comparing enum values.
- **Broker options instead of broker columns:** one JSON `BrokerOptions` value on scenarios and Publishers (RabbitMQ: `exchange`; later Service Bus: `subscription`, MQTT: `qos`/`retain`, …), validated by the adapter. `exchange` stays accepted everywhere it is today.
- **The UI asks the server** which types exist and what they need, rather than hard-coding them.

### Rules every adapter keeps (from `.claude/rules/infrastructure.md`)

- **Listen never takes messages from real consumers.** If a broker can only listen destructively (a plain queue: SQS, a Service Bus queue), its adapter reports "can't Listen" with the reason, and the UI says so. Listen goes through a fan-out mechanism (topic + temporary subscription) or not at all.
- **`onListening` fires exactly when the subscription is in place.** Suites start their Sends on it.
- **Short-lived clients, own timeouts, readable messages.** The adapter opens its own client for each run, never an Aspire-wired one. Connect/setup has a budget separate from the listen timeout. Messages are safe to show: no connection strings, no stack traces.
- **A publish is reported only once the broker has it** (NATS pings, RabbitMQ checks the exchange, Kafka awaits delivery).

## Refactoring steps

Each step is one PR, behaviour-preserving unless it says otherwise. All existing suites must stay green, which guards against regressions.

### R0. ADR 0003 — brokers and adapters (S)

Record the decisions and ask the open questions (see the end):
- which brokers, in which order;
- Listen semantics per broker;
- the `BrokerOptions` shape;
- how `exchange` stays compatible.

**Done when:** accepted.

### R1. Adapters behind the existing interfaces (M) — pure refactoring

- `Infrastructure/Brokers/IBrokerAdapter`, with `Type`, `TestAsync`, `SendAsync` and `ListenAsync` (the same signatures as today's interface methods).
- `Brokers/Http`, `Brokers/RabbitMq`, `Brokers/Nats`, `Brokers/Kafka`: today's branches moved in **unchanged**, together with `KafkaClients`/`RabbitMqConnections`. `Http` implements test and send; it has no Listen.
- `MessageSender`/`MessageListener`/`ConnectionTester` resolve the adapter from a registry built from DI (`IEnumerable<IBrokerAdapter>` → dictionary by type).
- Tests:
  - the existing tests are unchanged;
  - new: every `ConnectionServiceType` value has exactly one adapter, and no two adapters claim the same type.

**Done when:** no `switch` on `ConnectionServiceType` remains in `Infrastructure/Connections`, and unit, integration and E2E tests pass without edits (apart from namespaces).

### R2. Type descriptors in Domain, served to the UI (S)

- `Domain/Connections/ServiceTypeTraits`: per type — `IsHttp`, `CanListen` (+ the reason when not), display name, value hint.
- `OperationCompatibility.IsCompatible` and the "can listen" checks read the traits.
- `GET /api/system/connection-types` → `[{ type, displayName, valueHint, isHttp, canListen, listenNote }]`.
- Web: the Settings select and its placeholders come from that endpoint; Test Scenarios and Publishers filter connections by `isHttp`/`canListen` from it.
- A unit test keeps the traits and the adapters consistent: an adapter with no Listen ⇔ `CanListen = false`.

**Done when:** adding an enum value plus an adapter shows up in the UI with no Web change.

### R3. Subscription patterns per broker (S)

- Domain: `ChannelPattern` — the channel address split into literal segments and parameter positions. It replaces the shared string pattern from `TestScenarioListening.SubscriptionPatternOf`.
- Each adapter renders its own wildcard syntax:
  - RabbitMQ topic `*`;
  - NATS `*`;
  - Kafka regex (`TopicRegexOf` moves into the Kafka adapter);
  - later MQTT `+` and Redis glob.
- Separators: today `.` only. MQTT and many specs use `/`; the pattern keeps the address's own separator.
- Tests: the existing pattern tests per adapter, plus `/`-separated addresses.

**Done when:** no broker syntax is left in Domain, and current behaviour is unchanged for `.`-addresses.

### R4. `BrokerOptions` instead of `Exchange` (M) — compatible

- Domain:
  - `TestScenario.BrokerOptions` and `Publisher.BrokerOptions` (JSON object, nullable);
  - migration `MoveExchangeIntoBrokerOptions`: copy `Exchange` into `{"exchange": …}`, then drop the column.
- Application: create/update validate the options through the adapter (an Application interface, implemented by the registry) → `ArgumentException` → 400 with the reason, as now.
- **Compatibility (contract v1 must not break):**
  - API request/response DTOs keep `exchange` (read and written as `brokerOptions.exchange`) and gain `brokerOptions`;
  - manifest: `exchange` stays valid; `brokerOptions` is added (additive schema change);
  - the export writes `exchange` for RabbitMQ, so files stay readable by older images.
- Web: the RabbitMQ exchange field becomes an options editor driven by the adapter's option list (served with R2's endpoint).
- Tests:
  - the existing exchange tests (unit, `ListenScenarioApiTests`, provisioning, export round trip) pass unchanged;
  - new: unknown option → 400; `brokerOptions` round-trips through export.

**Done when:** RabbitMQ behaves exactly as before, and a new broker can add options without a migration.

### R5. Suggest the connection type from the spec (S)

- The AsyncAPI parser stores `servers.*.protocol` (`amqp`, `kafka`, `nats`, `mqtt`, `redis`, `sqs`, …) on the specification.
- The Test Scenario/Publisher forms put matching connections first and say "this spec is for Kafka" when nothing matches.

**Done when:** importing `shop-events-kafka-asyncapi.yaml` puts Kafka connections first.

### R6. Integration tests per broker without slowing CI (S)

- Today the AppHost starts RabbitMQ, NATS and Kafka for every integration and E2E run, and each new broker adds a container.
- The test fixture starts only the brokers a test collection needs:
  - one collection per broker family;
  - the AppHost resources stay as they are for local development.
- CI: the core brokers stay in the required "Integration tests" job; brokers added later run in a "Broker integration" matrix job. It isn't required for merging until it has proven stable.

**Done when:** adding a broker adds one CI matrix entry, not minutes to every run.

*Implementation note:* AppHost takes `Brokers` (all by default); `AppHostFixtureBase` boots only the brokers it's given, and a family's tests live in `tests/VroksNet.IntegrationTests/Brokers/<Family>`, which the required job skips by namespace. The `broker-integration` matrix job is in `ci.yml`, commented out until N2 adds its first entry. How to add a family: `.claude/rules/integration-tests.md`.

## New brokers on top (each its own PR, in this order unless ADR 0003 decides otherwise)

| Step | Broker | Size | Listen | Notes |
|---|---|---|---|---|
| N1 | Azure Event Hubs | S | via Kafka | Likely works already through the Kafka endpoint with SASL settings. Verify against the emulator, then document; no adapter |
| N2 | MQTT | S | yes: plain subscription | MQTTnet; `+`/`#` wildcards; `qos`/`retain` options; container: Mosquitto or EMQX |
| N3 | Redis | S | Pub/Sub: subscription; Streams: `XREAD` from `$`, no consumer group | StackExchange.Redis; official Aspire integration; option: `mode` (pubsub/stream) |
| N4 | Azure Service Bus | M | topics only, through a subscription (option `subscription`; a temporary one when the connection may manage entities) | Queues: Send only. Aspire emulator; its entities are declared up front |
| N5 | AWS SQS / SNS | M | SNS: a temporary SQS queue subscribed to the topic; plain SQS: Send only | LocalStack in tests |

**Checklist for each new broker:**
1. A `ConnectionServiceType` value. It's additive for the manifest schema's `type` enum and contract §5; tell the Aspire package repository.
2. An adapter: test, Send (confirmed), Listen with `onListening` (or "can't" with the reason), pattern rendering, options validation.
3. Traits in Domain.
4. Its connection-value format, matching what Aspire's `WithReference` hands out for that resource, since the Aspire package passes exactly that through `valueFrom`.
5. Unit tests against unreachable targets (the rule in `.claude/rules/unit-tests.md`).
6. An integration test in the broker matrix job (R6), covering Send, Listen and a suite whose Send is caught by its Listen.
7. Docs: runbook, API reference, the contract-testing guides, samples (an AsyncAPI spec for it), `.claude/rules/infrastructure.md`.

## Order

```
R0 → R1 → R2 → R3 → R4 → (R5, R6 in any order) → N1 … N5
```

R1–R3 can't change behaviour, so they're safe to do first. R4 is the only step with a migration and a public shape change, so its compatibility tests matter most. N1 needs none of the refactoring and can go first if Event Hubs users are waiting.

## Risks

- **Behaviour drift while moving code (R1).** Move without editing — one commit only moves, another wires the registry — and let the existing integration tests decide.
- **Breaking contract v1 (R4).** `exchange` must keep working in the API, the manifest and the export. A round-trip test on an old-style manifest guards it.
- **Destructive Listen sneaking in (N4, N5).** The rule is enforced by traits plus review. An adapter for a queue-only broker must say "can't Listen", never consume.
- **CI time (R6 before N2).** Without per-broker collections, each broker slows every run.

## Questions for ADR 0003 — resolved

ADR 0003 answers them:
1. Brokers and order: as in the table above.
2. Service Bus / SNS: both — a named existing `subscription`, or a temporary one when the connection may manage entities.
3. Queue-only brokers are Send only, and the UI says why Listen isn't offered.
4. `exchange` is deprecated in contract v1 and removed in v2.
5. WebSocket/SSE served by the mock: out of scope, a separate ADR if wanted.
