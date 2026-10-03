# ADR 0003 — Broker adapters, and which brokers come next

**Status:** Accepted (2026-10-03)
**Date:** 2026-10-03
**Related:** `docs/broker-adapters-plan.md` (the steps R1–R6 and N1–N5 that carry this out), ADR 0001 (provisioning manifest, contract v1), ADR 0002 (`onListening` and suites), `docs/container-contract.md` §5 (connection types), `.claude/rules/infrastructure.md` ("Connections")

## Context

VroksNet sends to and listens on three brokers today — RabbitMQ, NATS and Kafka — plus HTTP. Each one is a branch in several places rather than a unit of its own:

- `MessageSender`, `MessageListener` and `ConnectionTester` in `Infrastructure/Connections` each `switch` on `ConnectionServiceType`, with the client setup shared through `KafkaClients` and `RabbitMqConnections`.
- Domain decides compatibility by comparing enum values (`OperationCompatibility`: an HTTP operation needs an `Http` connection, an AsyncAPI operation any broker) and builds one subscription pattern for all brokers (`TestScenarioListening`), which Kafka then rewrites into a regex.
- `Exchange` — a RabbitMQ-only setting — is a column on `TestScenario` and `Publisher` and a field in the DTOs, the endpoints, the Web forms, the manifest and the export.
- The Web Settings page and the manifest schema list the types by hand.

Users want more brokers: MQTT, Redis, Azure Service Bus, AWS SQS/SNS, Azure Event Hubs. In the current shape each one touches about ten files in four projects. And some of them only offer queues, where receiving a message removes it — which would break the rule that Listen never takes messages from real consumers.

## Decision

### One adapter per broker

Each `ConnectionServiceType` gets one adapter in `VroksNet.Infrastructure/Brokers/<Broker>`, implementing `IBrokerAdapter`:

- the connection check;
- Send, reported as successful only once the broker has the message;
- Listen, calling `onListening` exactly when the subscription is in place — or a declared "can't Listen" with the reason;
- rendering a channel pattern in its own wildcard syntax;
- validating its own broker options.

`IMessageSender`, `IMessageListener` and `IConnectionTester` keep their Application signatures. Their Infrastructure implementations become dispatchers that look the adapter up in a registry built from DI (`IEnumerable<IBrokerAdapter>`, keyed by type). A unit test fails if a type has no adapter or two adapters. HTTP is an adapter too, with test and Send and no Listen.

The adapter rules from `.claude/rules/infrastructure.md` apply to every adapter: its own short-lived client per run (never an Aspire-wired one), a connect budget separate from the listen timeout, and messages safe to show.

### What a type can do is data in Domain

`ServiceTypeTraits` in `VroksNet.Domain.Connections` describes each type: whether it is HTTP, whether it can Listen (and why not), its display name and the hint for its connection value. Domain rules read the traits instead of comparing enum values. `GET /api/system/connection-types` serves them, and the Web UI builds its type list, placeholders and connection filters from that response. A unit test keeps traits and adapters consistent.

### Subscription patterns per broker

Domain keeps only the structure of a channel address — `ChannelPattern`: literal segments, parameter positions and the address's own separator (`.` or `/`). Each adapter renders it in its broker's syntax: `*` for RabbitMQ and NATS, a regex for Kafka, `+` for MQTT, a glob for Redis.

### `BrokerOptions` instead of broker columns

`TestScenario` and `Publisher` get one nullable JSON value, `BrokerOptions`, validated by the adapter of the connection's type through an Application interface. An unknown or invalid option is a 400 with the reason. `Exchange` moves into it as `{"exchange": …}` through a migration, and its column is dropped.

**Compatibility with contract v1.** `exchange` stays valid wherever it is accepted today — the API requests and responses, the manifest, the export:

- the API reads and writes it as `brokerOptions.exchange`, and adds `brokerOptions` next to it;
- the manifest schema adds `brokerOptions` (an additive change) and keeps `exchange`;
- the export writes `exchange` for RabbitMQ, so its files stay readable by older images.

`exchange` is **deprecated**: documented as such in contract v1 and the schema, and **removed in contract v2**. Setting both `exchange` and `brokerOptions.exchange` to different values is a 400.

### Listen never consumes

Listen goes through a fan-out mechanism — a topic or exchange with a temporary subscription, a plain broker subscription, or a non-destructive read — or it isn't offered.

- **Queue-only brokers** (plain SQS, Service Bus queues) are **Send only**. Their traits say `CanListen = false` with the reason, and the UI shows that reason instead of offering Listen. Peeking a queue isn't used for Listen: it sees messages already waiting, not the next one, and races with the real consumers.
- **Topics with subscriptions** (Service Bus topics, SNS) support **both** ways of listening:
  - if the broker options name a `subscription`, the adapter reads from that existing subscription;
  - otherwise the adapter creates a temporary subscription for the run and deletes it afterwards. This needs management rights on the connection; without them the run fails with a message saying to grant them or name a subscription.

  For SNS the temporary subscription is a temporary SQS queue subscribed to the topic.

### Which brokers, in which order

After the refactoring (plan steps R1–R6), in this order, each its own PR:

1. **Azure Event Hubs** — through its Kafka endpoint with SASL settings, so no adapter: verify against the emulator and document.
2. **MQTT** — plain subscriptions, `+`/`#` wildcards, options `qos`/`retain`.
3. **Redis** — Pub/Sub subscriptions, or Streams read with `XREAD` from `$` without a consumer group; option `mode`.
4. **Azure Service Bus** — Send to queues and topics; Listen on topics as above.
5. **AWS SQS / SNS** — Send to both; Listen on SNS as above.

Event Hubs needs none of the refactoring and may go earlier if someone needs it.

Each new type is additive for the manifest schema's `type` enum and for contract §5, and its connection value matches what Aspire's `WithReference` hands out for that resource.

### Out of scope

WebSocket and SSE channels served by the mock, the way provider mode serves HTTP, are a different feature — VroksNet would be the server, not a client of a broker. If wanted, they get an ADR of their own.

## Consequences

- A new broker is an enum value, an adapter, its traits, tests and docs. Application, the endpoints and the Web UI don't change.
- The R1 move is behaviour-preserving and checked by the existing unit, integration and E2E tests, unchanged.
- The `Exchange` migration is the only data change. Old manifests and exports keep working, and a round-trip test on an old-style manifest guards this until v2.
- Temporary subscriptions need management rights on Service Bus and SNS connections. A user without them can still name an existing subscription.
- Each new broker adds a container to the test run. Collections per broker family and a separate CI matrix job (plan step R6) keep that out of the required job.
