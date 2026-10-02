# Contract testing with VroksNet

VroksNet checks that services keep to their OpenAPI and AsyncAPI contracts. You import a spec, tell VroksNet where the service (or broker) lives, and it either calls the service, publishes to it, listens to it, or lets the service call a mock, and checks the traffic against the spec. Every call ends up in **Call History** with the result of the contract check.

## Start here

- [Getting started](getting-started.md): running VroksNet, the main concepts (specifications, connections, test scenarios, Call History) and the addresses you'll use.

## The four kinds of test

| # | You want to check… | Direction | What's validated | Guide |
|---|---|---|---|---|
| 1 | that a service's **HTTP responses** match its OpenAPI spec | VroksNet → service | response body, per status code | [Type 1: Consumer test (HTTP)](type-1-consumer-http.md) |
| 2 | that a service **accepts the messages** its AsyncAPI spec says it consumes | VroksNet → broker | nothing (fire-and-forget) | [Type 2: Producer test (publish)](type-2-producer-publish.md) |
| 3 | that a service **sends correct HTTP requests** to a dependency | service → VroksNet mock | incoming request body | [Type 3: Provider test (HTTP mock)](type-3-provider-http.md) |
| 4 | that a service **publishes messages** matching its AsyncAPI spec | broker → VroksNet | message payload | [Type 4: Provider test (listen)](type-4-provider-listen.md) |

Types 1, 2 and 4 are **test scenarios** you save once and run on demand (from the UI or the API). Type 3 is a **mode** you switch on for an operation; from then on every call the service makes is checked as it happens.

## Picking the right type

- Your service **exposes** a REST API → **Type 1**: VroksNet calls it and checks the responses.
- Your service **calls** a REST API → **Type 3**: point it at VroksNet's mock instead and check its requests.
- Your service **consumes** messages → **Type 2**: VroksNet publishes example messages to it.
- Your service **produces** messages → **Type 4**: VroksNet listens on the channel and checks what arrives.

## Reference

- [Call History](call-history.md): finding a run or mock call, reading contract violations, clearing the history.
- [API quick reference](api-reference.md): every endpoint used in these guides.
- `docs/contract-testing-plan.md` (Russian): design notes and decisions behind each feature.
