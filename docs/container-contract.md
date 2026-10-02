# VroksNet image contract

**Contract version:** 1 (draft)
**Status:** some items already work, some are still to be built; each table marks them ("exists" / "to add")
**Decision:** ADR 0001 (`docs/adr/0001-aspire-integration-and-provisioning.md`)

This document fixes everything that code **outside** the container may rely on: ports, paths, environment variables, operational endpoints, the provisioning manifest format and exit codes. The main consumer is the Aspire hosting package, which lives in a **separate repository**. It must be able to start and configure VroksNet without knowing anything about the app's internals.

Not covered here: the Admin UI's REST API (`docs/guides/contract-testing/api-reference.md`). That's an interface for people and scripts, and the package doesn't rely on it.

## 1. Version and compatibility

- **The contract has a single integer version** — `1`. The image reports it in the `io.vroksnet.contract.version` label and in `GET /api/system/info` (`contractVersion`).
- **Without bumping the version, only additions are allowed:** new optional variables, response fields, manifest keys with safe defaults.
- **A breaking change bumps the version.** That's a removal or rename, a change of a default or of meaning, or a new required key. The change is recorded in a CHANGELOG, and the package checks the version before starting.
- **The manifest is versioned separately**, by its `version` field. An image with contract `N` accepts manifests of versions `1..N`.

## 2. Image

| What | Value | Status |
|---|---|---|
| Name | `ghcr.io/versussun/vroksnet` (the registry is an open question in ADR 0001) | to add |
| Tags | `X.Y.Z`, `X.Y`, `latest`; `latest` only for releases from `master` | to add |
| Label `org.opencontainers.image.version` | the app version | to add |
| Label `org.opencontainers.image.source` | the repository URL | to add |
| Label `io.vroksnet.contract.version` | the version of this contract (`1`) | to add |
| User | unprivileged, with a documented UID. Mounted volumes must be accessible to it | to add |
| Entry point | `tini` → `dotnet VroksNet.ApiService.dll`. `SIGTERM` shuts the process down cleanly | exists |

## 3. Ports

| Port | Protocol | What | Recommended Aspire endpoint name | Status |
|---|---|---|---|---|
| `8080` | HTTP | REST API, Admin UI, `/health`, `/alive`, mocks under `/mock/…` | `http` | exists |
| `7353` | HTTP | provider mode: mocks at the spec's real paths | `provider` | exists |

The container speaks only HTTP. TLS is the job of a proxy or the orchestrator.

## 4. File system

| Path | Mode | What | Status |
|---|---|---|---|
| `/app/data` | read-write, `VOLUME` | SQLite (`vroksnet.db`). Without a mounted volume, data lives until the container is recreated — which is what tests want | exists |
| `/app/provisioning` | read-only | the provisioning root (`Provisioning:Path`). No directory — no provisioning | to add |
| `/app/provisioning/specs/**` | read-only | specs: `*.yaml`, `*.yml`, `*.json`, nested directories allowed. The kind is detected by the root key (`openapi` / `swagger` / `asyncapi`), not by the file name | to add |
| `/app/provisioning/vroksnet.yaml` | read-only | the manifest, optional. Schema: `docs/schemas/provisioning-manifest.v1.schema.json`; a copy ships in the image at `/app/provisioning-manifest.v1.schema.json` | to add |

The package mounts the specs and the manifest **separately** (a bind mount of a file or a directory). So `specs/` and `vroksnet.yaml` must work independently of each other.

## 5. Environment variables

Names are given in .NET environment-variable form (`:` → `__`).

### Existing

| Variable | Image default | What |
|---|---|---|
| `ASPNETCORE_HTTP_PORTS` | `8080` | the API and UI port |
| `ConnectionStrings__VroksNetDb` | `Data Source=/app/data/vroksnet.db` | storage. Empty — in-memory SQLite; data lives until the process stops |
| `Provider__Port` | `7353` | the provider mode port |
| `Provider__PublicUrl` | — | the provider port's address **as the user sees it** (the Admin UI shows it in hints). The package passes the external URL of the `provider` endpoint here |
| `Provider__CorsOrigins` | — (CORS off) | comma-separated origins, or `*`, for browser frontends calling the mock |
| `OTEL_EXPORTER_OTLP_ENDPOINT` and other `OTEL_*` | — | exporting logs, traces and metrics (ServiceDefaults). The package calls `WithOtlpExporter()`, and the telemetry shows up in the Aspire dashboard |

### To add

| Variable | Default | What |
|---|---|---|
| `Provisioning__Path` | `/app/provisioning` | the provisioning root |
| `Provisioning__FailOnError` | `true` | a provisioning error stops the process (§7). `false` — log it, report `Failed` in `/api/system/info`, keep running |
| `Provisioning__Connections__<i>__Name` | — | a connection declared **without a manifest**, where `<i>` = 0, 1, …. This is how the package passes `WithConnection(...)` without generating files. Indexes rather than names in the key: a connection name may contain `-`, which isn't valid in an environment variable name for POSIX shells |
| `Provisioning__Connections__<i>__Type` | — | `Http` / `RabbitMq` / `Nats` / `Kafka` |
| `Provisioning__Connections__<i>__Value` | — | the connection value as-is |
| `Provisioning__Connections__<i>__ValueFrom` | — | a configuration key to take the value from, e.g. `ConnectionStrings:kafka` (set by Aspire's `WithReference(kafka)`). Exactly one of `Value` and `ValueFrom` |
| `ConnectionStrings__<name>` | — | connection strings to brokers and services that `valueFrom` refers to. VroksNet itself doesn't read them |

**Connections from the two sources are merged:** from the manifest and from `Provisioning__Connections__*`. The same name in both sources is a provisioning error. Neither source silently wins.

## 6. Operational endpoints

| Endpoint | Response | Purpose | Status |
|---|---|---|---|
| `GET /alive` | `200` / `503` | liveness: the process is alive | exists |
| `GET /health` | `200 Healthy` / `503 Unhealthy` | readiness. **Once extended**, Unhealthy until provisioning has been applied. This is what `WaitFor(mocks)` waits for | exists; behavior to add |
| `GET /api/system/info` | see below | version, contract, provisioning state. The package shows it in the dashboard; consumers' tests wait on it | to add |
| `GET /api/system/provider` | `{ enabled, port, publicUrl, corsOrigins }` | provider mode settings | exists |

The `GET /api/system/info` response:

```json
{
  "version": "1.4.0",
  "contractVersion": 1,
  "provisioning": {
    "status": "Applied",
    "appliedAt": "2026-10-02T18:30:00Z",
    "source": "/app/provisioning",
    "counts": { "specifications": 3, "connections": 2, "publishers": 1, "testScenarios": 1 },
    "errors": []
  }
}
```

`status`: `NotConfigured` (no directory) · `Applying` · `Applied` · `Failed`. Each `errors[]` item is `{ "source": "specs/payments.yaml", "message": "…" }`. A connection value never appears in `message`.

## 7. Startup and exit codes

Startup order:
1. database migrations;
2. the write queue starts;
3. provisioning — specs, then connections, then everything else;
4. `/health` → Healthy.

The API answers already during step 3, but `/health` returns `503` until provisioning finishes.

| Code | When |
|---|---|
| `0` | normal shutdown (`SIGTERM`) |
| `3` | provisioning failed with `Provisioning__FailOnError=true`. Every error is logged with its file and key. **To add** |
| `134` | an unhandled exception (`SIGABRT` through `tini`) |

Starting again with the same contents of `/app/provisioning` creates no duplicates. Provisioned objects are brought in line with the manifest; objects created in the UI aren't touched (ADR 0001).

## 8. How the Aspire package uses this (for reference)

The package lives in its own repository. The table shows which contract item each of its methods relies on — and that the package needs nothing beyond this document.

| Package method | Contract item |
|---|---|
| `AddVroksNet(name, tag)` | §2 the image and contract label; §3 the `http`/`provider` endpoints; §6 `/health`; `Provider__PublicUrl` = the external URL of `provider` |
| `WithSpecifications(dir)` | §4 a bind mount into `/app/provisioning/specs` |
| `WithProvisioning(file)` | §4 a bind mount into `/app/provisioning/vroksnet.yaml` |
| `WithConnection(name, resource)` | `WithReference(resource)` → `ConnectionStrings__<resource>`; §5 `Provisioning__Connections__<i>__*` with `ValueFrom` |
| `WithDataVolume()` | §4 a volume on `/app/data` |
| `WithProviderCors(origins)` | §5 `Provider__CorsOrigins` |
| telemetry in the dashboard | §5 `OTEL_*` via `WithOtlpExporter()` |
| waiting for readiness | §6 `/health`; details in `/api/system/info` |

**Addresses on the container network.** VroksNet runs in a container, so `ConnectionStrings__<resource>` must hold the broker's address **on the container network** (`kafka:9093`), not the host's. That's the package's and Aspire's responsibility, but it's verified with a prototype before the package is released (ADR 0001, risks).

## 9. How the contract is verified in this repository

A CI job after `docker build` (**to add**):
1. run the image with `docs/samples/` as `/app/provisioning/specs` and with `Provisioning__Connections__0__*`;
2. wait for `/health`;
3. check `GET /api/system/info`: `contractVersion`, `status = Applied`, the number of specs;
4. check the `io.vroksnet.contract.version` label;
5. run it with a deliberately broken spec and check exit code `3`.

That way a change that breaks the contract fails here, not in the package repository.
