# ADR 0001 — VroksNet as an Aspire resource, provisioned at startup

**Status:** Accepted (2026-10-02); questions resolved 2026-10-03, except where the NuGet package is published
**Date:** 2026-10-02
**Related:** `docs/container-contract.md` (the image contract), `docs/schemas/provisioning-manifest.v1.schema.json` (manifest schema), `docs/project-brief.md` (§3 "Deployment: one process, one image"), `docs/samples/README.md`

## Context

Today VroksNet is configured by hand. Specs are imported, broker and HTTP connections created, Publishers and Test Scenarios set up and provider mode switched on through the Admin UI or the REST API, one object per call. That's fine for one long-lived installation. But teams want to run VroksNet **next to their own services in their own Aspire AppHost**: as a mocked dependency in dev, and in integration tests built on `Aspire.Hosting.Testing`. Every such run starts with an empty database, and configuring it by hand each time isn't acceptable.

The experience a consuming team should get:

```csharp
var kafka = builder.AddKafka("kafka");

var mocks = builder.AddVroksNet("mocks")
    .WithSpecifications("./mocks/specs")
    .WithConnection("orders-kafka", kafka)
    .WithProvisioning("./mocks/vroksnet.yaml");

builder.AddProject<Projects.Orders>("orders")
    .WithEnvironment("Payments__BaseUrl", mocks.GetEndpoint("provider"))
    .WaitFor(mocks);
```

What stands in the way in the current code:

1. **The image isn't published anywhere.** It can only be built locally (`docker build`); there's no CI.
2. **There's no provisioning mechanism.** Everything is created through API calls after startup.
3. **The API references objects by GUID.** Publishers and Test Scenarios store `SpecificationId`/`MockEndpointId`/`ConnectionId`, which can't be known in advance when writing a config.
4. **Only two keys are unique:** `ApiSpecification.Title` and `Connection.Name`. Publisher and Test Scenario names aren't, so applying a config twice would create duplicates.
5. **Startup order.** `InitializeDatabaseAsync()` runs before `app.Run()`, but every write goes through `IDbWriteQueue`, which `DbWriteBackgroundService` drains — and that only starts with the host. A write queued before the host starts would hang.
6. **`/health` is healthy right after startup.** A consumer's `WaitFor(mocks)` would release its service before the mocks are ready.

## Ownership boundary

The Aspire hosting package is developed **in a separate repository**. This repository owns the image and its **contract**: everything the package may rely on from outside the container —
- ports, paths and environment variables;
- operational endpoints;
- the manifest format and exit codes.

The contract is written down in `docs/container-contract.md` and versioned (label `io.vroksnet.contract.version`). The manifest schema is `docs/schemas/provisioning-manifest.v1.schema.json`.

The package doesn't call the Admin UI's REST API and knows nothing about the app's internals. If the package needs something beyond the contract, the contract changes here first.

## Decision

Three layers in this repository, plus the package in its own. Each layer is useful on its own; each builds on the previous one.

### 0. Publish the image

GitHub Actions on `master` and on tags: build, unit and integration tests, `docker build`, push to GHCR (`ghcr.io/versussun/vroksnet`, tags `latest` and `vX.Y.Z`). Without this the Aspire package has nothing to run.

### 1. Provisioning inside the app

Works however the image is run: `docker run`, docker-compose, Kubernetes, Aspire.

**Source:** a directory, set by `Provisioning:Path`, default `/app/provisioning`. No directory — no provisioning; behavior is unchanged.

```
/app/provisioning/
  specs/**/*.{yaml,yml,json}   ← all imported; kind detected by the root key openapi / swagger / asyncapi
  vroksnet.yaml                ← the manifest, optional
```

**The manifest.** Everything in it is referenced by name, secrets come from configuration, and the format is versioned:

```yaml
version: 1

connections:
  - name: orders-kafka
    type: Kafka
    valueFrom: ConnectionStrings:kafka      # a configuration key; Aspire sets it via WithReference
  - name: payments-http
    type: Http
    value: http://payments:8080

specifications:                              # the specs themselves come from specs/; only their settings live here
  - title: Payments Sample API
    providerMode: true
    disabledOperations: ["DELETE /payments/{paymentId}"]

publishers:
  - name: order-created-every-5s
    specification: Shop Events Kafka Sample  # info.title
    operation: "shop.orders.created:send"    # OperationKey
    connection: orders-kafka
    intervalSeconds: 5
    enabled: true

testScenarios:
  - name: payments-contract
    specification: Payments Sample API
    operation: "GET /payments/{paymentId}"
    connection: payments-http
```

**How it's applied:**

- **Where the code lives (per the repo's layering):**
  - Infrastructure — `ProvisioningHostedService` (an `IHostedService` registered **after** `DbWriteBackgroundService`) and reading the files (`IProvisioningSource`);
  - Application — a single Mediator request `ApplyProvisioning` (`sealed record`), whose handler reuses the existing use cases: `ImportOpenApiSpec`/`ImportAsyncApiSpec`, creating and updating connections, Publishers and scenarios;
  - ApiService — registration only; no business logic in Presentation.
- **Applying it again creates no duplicates:**
  - specs are already replaced by `info.title` (the brief's versioning rule);
  - connections are updated by their unique `Name`;
  - Publishers and Test Scenarios get a **unique name** (a migration with a unique index) and are updated by it too.
- **The file wins over the UI:** on every start, provisioned objects are brought back in line with the manifest. Objects created in the UI aren't touched. (Implementation note, A3: an object removed from the manifest is left in place, still marked provisioned — provisioning never deletes, so a typo in the manifest can't wipe data.) Provisioned ones are marked (`ProvisionedAt` or a flag), and the UI shows them with a "managed by provisioning" badge.
- **Readiness:** a `provisioning` health check reports Unhealthy until provisioning has been applied successfully. `/health` aggregates all checks, so Aspire's `WaitFor` and a Kubernetes readiness probe both wait for the mocks to be loaded.
- **Errors fail startup by default:** a broken spec, an unknown name in a reference, an unknown manifest key, an empty `valueFrom`. The log says exactly what failed: file, key and name. A mock with half its specs is more dangerous than a crashed container: the consumer's tests would fail in confusing ways. `Provisioning:FailOnError=false` softens this to log-and-continue (for a shared long-lived installation).
- **No secrets in the manifest:** `valueFrom` points at a configuration key (environment variable, user secrets, Aspire `WithReference`), and the value is read from there.
- **Connections can also be declared without a manifest**, through `Provisioning__Connections__<i>__Name/Type/Value/ValueFrom` variables. That's how the package passes `WithConnection(...)` without generating anything on disk. Connections from the manifest and from variables are merged; the same name in both is an error.
- **State visible from outside:** `GET /api/system/info` returns the app version, the contract version, the provisioning status (`NotConfigured` / `Applying` / `Applied` / `Failed`) and any errors. A failed provisioning with `FailOnError=true` exits the process with **code 3**.

Exact names, defaults and formats are in `docs/container-contract.md`.

### 2. Aspire hosting package — in a separate repository

A thin wrapper over layer 1. It configures nothing through the API: it only mounts files and passes configuration to the container, per the contract. The table below is the package's target API; which contract item each method relies on is laid out in `docs/container-contract.md` §8.

| API | What it does |
|---|---|
| `AddVroksNet(name, tag?)` | a container from the published image; endpoints `http` (8080) and `provider` (7353); HTTP health check on `/health`; Admin UI URL in the dashboard |
| `WithSpecifications(dir)` / `WithSpecification(file)` | bind mount into `/app/provisioning/specs` |
| `WithProvisioning(manifestFile)` | bind mount of the manifest |
| `WithConnection(name, IResourceWithConnectionString)` | `WithReference(resource)` + `Provisioning__Connections__<i>__*` with `ValueFrom=ConnectionStrings:<resource>` |
| `WithDataVolume()` | SQLite on a volume. Without it nothing persists: every run starts clean, which is what tests want |
| `GetEndpoint("provider")` | the mock at real spec paths — handed to services in place of the real dependency |

The package depends only on `Aspire.Hosting`. It declares which contract version it supports and checks the image label for compatibility before starting it. The default image tag is set in the package and moves with its releases.

### 3. Export the current configuration

`GET /api/provisioning/export` returns a zip of `specs/` and a `vroksnet.yaml` built from the current state (secrets replaced by `valueFrom` placeholders), plus an "Export" button on Settings. The workflow: configure in the UI, export, commit to the consumer's repository. Without export, manifests are written by hand and the barrier to entry stays high.

(Implementation note, A7:
- **Every connection value is treated as a secret.** It becomes `valueFrom: ConnectionStrings:<name>`, with the name reduced to `[A-Za-z0-9_-]`, and the file's header lists the variables to set. `?inlineValues=true` (the "Export with connection values" button) keeps the values, for a file that stays private.
- **Objects whose spec, operation or connection is gone are left out,** with a note in the header, so the export always provisions cleanly.
- **Provider mode is all-or-nothing in the manifest.** A spec serving only some operations at their real paths is exported as `providerMode: true`, with a note.
- **Tested as a round trip:** export, provision an empty instance from it, get the same objects back.)

## Options considered

### A. Only an Aspire package that configures everything through the REST API after the container starts

- **Pros:** no changes to the app itself.
- **Cons:**
  - works only from .NET and Aspire; docker-compose and Kubernetes get nothing;
  - the package would have to map names to GUIDs itself over several calls;
  - readiness would have to be stitched together from Aspire events, while the container's own `/health` kept lying.
- **Rejected** as the primary mechanism. Kept as a fallback if layer 1 turns out not to fit.

### B. Ship a ready-made database (a SQLite file snapshot)

- **Pros:** minimal code.
- **Cons:**
  - a snapshot is tied to a schema version, so old snapshots break after migrations;
  - a binary file can't be reviewed in git;
  - connection secrets sit right inside it.
- **Rejected.**

### C. Environment variables only, no manifest file

For example `Provisioning__Publishers__0__Name=…`.

- **Pros:** native to .NET configuration and Kubernetes.
- **Cons:** Publishers and scenarios that reference each other turn into an unreadable wall of indexes.
- **Partly adopted:** the manifest can reference configuration through `valueFrom`, and connections can come from variables, but everything else is described in the file.

### D. A consumer's own image (`FROM vroksnet` + `COPY specs/ /app/provisioning/specs/`)

Not an alternative but an **additional delivery method** for layer 1, for CI without bind mounts. Works automatically; no extra code needed.

### E. Load a spec from a URL on a live service (`WithSpecificationFrom(orders, "/openapi/v1.json")`)

- **Pros:** the mock always matches the service's current spec.
- **Cons:** introduces a startup-order dependency (the mock waits for the service, and the service may wait for the mock), plus retries and timeouts.
- **Deferred**; can be revisited after layer 2.

## Consequences

**Positive**
- A mock with its specs and brokers comes up from one declaration, the same way in dev, CI and docker-compose.
- The mock configuration lives in git in the consumer's repository and is reviewed with the code.
- `/health` starts meaning "ready to work", not just "the process is alive".

**Negative and risks**
- **The unique-name migration for Publishers and Test Scenarios** has to resolve existing duplicates in persistent databases, e.g. by appending ` (2)` to the name. Without that the migration fails.
- **UI edits to provisioned objects are lost** on restart. That's deliberate (the file wins) and visible through the badge. It needs to be documented in the runbook.
- **Startup takes longer** by the time it takes to import the specs: going by `SampleSpecificationsTests`, a fraction of a second per spec.
- **Container-to-container broker addresses (the main technical risk).** VroksNet runs in a container, so a broker connection string must point at the address on the container network (`kafka:9093`), not at the host's `localhost:<port>`. Aspire 13 should substitute the right address for a container-to-container reference. But Kafka has two listeners (one for the host, one for containers), so this is **verified with a prototype before the package is written** (plan step 5, in the package repository).
- **New public surface:** the manifest format and the package API. Both must be versioned (`version: 1`) and kept backward compatible.

## Implementation plan

**In this repository**

1. **CI and image publishing**, with OCI labels including `io.vroksnet.contract.version`.
2. **Layer 1** — the manifest and its schema validation, connections from environment variables, `ApplyProvisioning`, unique names (migration), `ProvisioningHostedService`, the health check, `GET /api/system/info`, exit code 3, a `docs/samples/provisioning/` example, a runbook section.
3. **Contract verification in CI** (`docs/container-contract.md` §9): run the image with the specs from `docs/samples/`, check `/health`, `/api/system/info`, the label, and the exit code on a broken spec.
4. **Layer 3** — export.

**In the package repository** (after steps 1–2)

5. **A container-to-container prototype** for Kafka, RabbitMQ and NATS: the VroksNet container plus brokers in one AppHost, Send and Listen through `ValueFrom`. If it uncovers a problem on the image side, it's solved by changing the contract in this repository.
6. **The package itself** and a sample consuming AppHost.

## Resolved questions

Answered on 2026-10-03:

1. **Does the file win over the UI?** **Yes.** On every start, provisioned objects are brought back in line with the manifest, and UI edits to them are lost; the UI marks them "managed by provisioning". This is consistent with re-importing a spec updating it.
2. **Image registry:** **GHCR**, `ghcr.io/versussun/vroksnet` — published from GitHub Actions, next to the repository. It's the default in `AddVroksNet`.
3. **`POST /api/system/reset`:** **not now.** Every AppHost run starts with a clean database unless `WithDataVolume()` is used, and consumers' tests can isolate themselves with unique names, as this repository's own tests do. Revisit if a consumer has many tests sharing one run that can't do that.

## Open questions

1. **Where to publish the NuGet package:** nuget.org, GitHub Packages or an internal feed? Deferred — to be decided in the package repository before its first release. It affects where people find the contract docs, so the package README should link to them.
