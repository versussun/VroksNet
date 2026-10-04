# VroksNet image contract

**Contract version:** 1 (draft)
**Status:** implemented; each table marks its items ("exists"), and CI verifies the image against it (§9)
**Decision:** ADR 0001 (`docs/adr/0001-aspire-integration-and-provisioning.md`)

This document fixes everything that code **outside** the container may rely on: ports, paths, environment variables, operational endpoints, the provisioning manifest format and exit codes. The main consumer is the Aspire hosting package, which lives in a **separate repository**. It must be able to start and configure VroksNet without knowing anything about the app's internals.

Not covered here: the Admin UI's REST API (`docs/guides/contract-testing/api-reference.md`). That's an interface for people and scripts, and the package doesn't rely on it.

## 1. Version and compatibility

- **The contract has a single integer version** — `1`. The image reports it in the `io.vroksnet.contract.version` label and in `GET /api/system/info` (`contractVersion`).
- **Without bumping the version, only additions are allowed:** new optional variables, response fields, manifest keys with safe defaults.
- **A breaking change bumps the version.** That's a removal or rename, a change of a default or of meaning, or a new required key. The change is recorded in a CHANGELOG, and the package checks the version before starting.
- **The manifest is versioned separately**, by its `version` field. An image with contract `N` accepts manifests of versions `1..N`.
- **Deprecated in v1, removed in v2:** the `exchange` field of publishers and test scenarios — in the manifest, the API and the export. Use `brokerOptions.exchange` (ADR 0003). Until v2 both are accepted, and the export keeps writing `exchange` so older images can read it.

## 2. Image

| What | Value | Status |
|---|---|---|
| Name | `ghcr.io/versussun/vroksnet` (GHCR, per ADR 0001), for `linux/amd64` and `linux/arm64` | exists |
| Tags | a release tag `vX.Y.Z` publishes `X.Y.Z`, `X.Y` and `latest` — so `latest` is always a release. Every push to `master` publishes `master` and `sha-<commit>` | exists |
| Label `org.opencontainers.image.version` | the app version: `X.Y.Z` for a release, `0.0.0-master.<commit>` for a `master` build | exists |
| Label `org.opencontainers.image.source` | the repository URL | exists |
| Label `io.vroksnet.contract.version` | the version of this contract (`1`) | exists |
| User | unprivileged `app`, UID `1654` (from the `aspnet` base image). `/app/data` belongs to it, so a new named volume is writable; a volume from an older root-running image must be `chown`ed to `1654` | exists |
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
| `/app/provisioning` | read-only | the provisioning root (`Provisioning:Path`). No directory — no provisioning | exists |
| `/app/provisioning/specs/**` | read-only | specs: `*.yaml`, `*.yml`, `*.json`, nested directories allowed. The kind is detected by the root key (`openapi` / `swagger` / `asyncapi`), not by the file name | exists |
| `/app/provisioning/vroksnet.yaml` | read-only | the manifest, optional. Schema: `docs/schemas/provisioning-manifest.v1.schema.json`; a copy ships in the image at `/app/provisioning-manifest.v1.schema.json` | exists |

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
| `Provisioning__Path` | `/app/provisioning` | the provisioning root |
| `Provisioning__FailOnError` | `true` | a provisioning error stops the process (§7). `false` — log it, report `Failed` (and `/health` `Degraded`, still 200), keep running with what applied |
| `Provisioning__Connections__<i>__Name` | — | a connection declared **without a manifest**, where `<i>` = 0, 1, …. This is how the package passes `WithConnection(...)` without generating files. Indexes rather than names in the key: a connection name may contain `-`, which isn't valid in an environment variable name for POSIX shells |
| `Provisioning__Connections__<i>__Type` | — | `Http` / `RabbitMq` / `Nats` / `Kafka` / `Mqtt` / `Redis` / `ServiceBus` (the last three added in v1 — additive) |
| `Provisioning__Connections__<i>__Value` | — | the connection value as-is |
| `Provisioning__Connections__<i>__ValueFrom` | — | a configuration key to take the value from, e.g. `ConnectionStrings:kafka` (set by Aspire's `WithReference(kafka)`). Exactly one of `Value` and `ValueFrom` |
| `ConnectionStrings__<name>` | — | connection strings to brokers and services that `valueFrom` refers to. VroksNet itself doesn't read them |

**Connections from the two sources are merged:** from the manifest and from `Provisioning__Connections__*`. The same name in both sources is a provisioning error. Neither source silently wins.

**Provisioning is configured** when the provisioning directory exists **or** at least one `Provisioning__Connections__<i>` is set. With variables alone (no directory) the report's `source` is `configuration`. Errors in a variable-declared connection name the variable, e.g. `Provisioning__Connections__2`; the same name in both sources is reported as `connections[<name>]`.

## 6. Operational endpoints

| Endpoint | Response | Purpose | Status |
|---|---|---|---|
| `GET /alive` | `200` / `503` | liveness: the process is alive | exists |
| `GET /health` | `200 Healthy` / `503 Unhealthy` | readiness: Unhealthy until provisioning has been applied (Healthy at once when there's no provisioning directory; `Degraded`, still 200, when it failed with `FailOnError=false`). This is what `WaitFor(mocks)` waits for | exists |
| `GET /api/system/info` | see below | version, contract, provisioning state. The package shows it in the dashboard; consumers' tests wait on it | exists |
| `GET /api/system/provider` | `{ enabled, port, publicUrl, corsOrigins }` | provider mode settings | exists |
| `GET /api/system/connection-types` | `[{ type, displayName, valueLabel, valueHint, isHttp, canListen, listenNote, options: [{ name, label, sendDescription, sendPlaceholder, listenDescription, listenPlaceholder, suggestedValue, allowedValues }] }]` | the connection types this image supports, what each can do, and the `brokerOptions` each accepts (ADR 0003). `sendDescription`/`sendPlaceholder` are null for an option that only applies to Listen (ServiceBus `subscription`), `listenDescription`/`listenPlaceholder` for one that only applies to Send. New types are added to the list, never removed within a contract version | exists |

The `GET /api/system/info` response:

```json
{
  "version": "1.4.0",
  "contractVersion": 1,
  "provisioning": {
    "status": "Applied",
    "appliedAt": "2026-10-02T18:30:00Z",
    "source": "/app/provisioning",
    "counts": { "specifications": 3, "connections": 2, "publishers": 1, "testScenarios": 1, "testSuites": 1 },
    "errors": []
  }
}
```

`counts.testSuites` was added with the manifest's `testSuites` (an addition, so still contract 1). `version` is the image's `VERSION` build arg (`0.0.0-dev` for local builds). `status`: `NotConfigured` (no directory and no `Provisioning__Connections__*`) · `Applying` · `Applied` · `Failed`. Each `errors[]` item is `{ "source": "specs/payments.yaml", "message": "…" }`. A connection value never appears in `message`.

## 7. Startup and exit codes

Startup order:
1. database migrations;
2. the write queue starts;
3. provisioning — specs, then connections, then everything else;
4. `/health` → Healthy.

The API answers already during step 3, but `/health` returns `503` until provisioning finishes.

| Code | When | Status |
|---|---|---|
| `0` | normal shutdown (`SIGTERM`) | exists |
| `3` | provisioning failed with `Provisioning__FailOnError=true`. Every error is logged with its file or manifest entry | exists |
| `134` | an unhandled exception (`SIGABRT` through `tini`) | exists |

Starting again with the same contents of `/app/provisioning` creates no duplicates. Provisioned objects are brought in line with the manifest; objects created in the UI aren't touched (ADR 0001). An object removed from the manifest is left in place — provisioning never deletes.

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

`scripts/verify-container-contract.sh <image>` checks a built image. The *Publish image* workflow builds the amd64 image, runs the script, and pushes nothing unless it passes — on pull requests that touch the image, the samples or the schema, too. It checks:
1. §2/§3: the `io.vroksnet.contract.version` label equals `ContainerContract.Version` in the code, the OCI labels are set, the user is `1654`, the entry point is `tini`, ports `8080`/`7353` are exposed, `/app/data` is a volume, and the shipped schema matches `docs/schemas/`;
2. §4–§6: with every `docs/samples/*.yaml` mounted file by file into `/app/provisioning/specs`, `docs/samples/provisioning/vroksnet.yaml` as the manifest and `Provisioning__Connections__0__*`: `/health` reaches 200, `/alive` is 200, `GET /api/system/info` reports `contractVersion`, a version, `status = Applied`, `source`, the expected counts and no errors;
3. §7: `docker stop` exits `0`; a second container on the same volume creates no duplicates;
   and the manifest's `runOnStartup` suite (`smoke`) runs after provisioning and passes;
4. §5: variables alone (no directory) provision, with `source = configuration`; nothing mounted gives `NotConfigured` and a healthy app;
5. §7: a broken spec stops the container with exit code `3` and the log names the file; so does a connection named in both the manifest and a variable.

That way a change that breaks the contract fails here, not in the package repository. Run it locally: `docker build -t vroksnet:local . && scripts/verify-container-contract.sh vroksnet:local` (needs `curl` and `jq`).
