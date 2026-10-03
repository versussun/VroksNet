# VroksNet runbook

How to deploy, configure, back up, upgrade and troubleshoot a VroksNet instance. For how to *use* it (specifications, mocks, contract tests), see the [contract testing guides](guides/contract-testing/README.md).

## At a glance

| | |
|---|---|
| What runs | One container, one process: `VroksNet.ApiService` serves the REST API, the Admin UI (static Blazor WebAssembly files) and the mocks |
| Ports | `8080`: API, Admin UI and `/mock/…`. `7353`: provider port (mocks at their real paths). Both plain HTTP |
| State | One SQLite file, `/app/data/vroksnet.db`, on the `/app/data` volume |
| Background work | Publisher worker (publishes due async mocks once a second) and the single database writer |
| External dependencies | None to start. RabbitMQ, NATS, Kafka and HTTP services are only reached through connections created on the **Settings** page |
| Authentication | None. Anyone who can reach the ports can change everything |
| Instances | Exactly one. See [Constraints](#constraints) |

## Deploy

```bash
docker run -d --name vroksnet --restart unless-stopped \
  -p 8080:8080 -p 7353:7353 \
  -v vroksnet-data:/app/data \
  -e Provider__PublicUrl=http://<host>:7353 \
  ghcr.io/versussun/vroksnet:latest
```

- **The image:** `ghcr.io/versussun/vroksnet`, for `linux/amd64` and `linux/arm64`.
  - `latest` is the newest release, and `X.Y.Z` / `X.Y` are releases.
  - `master` and `sha-<commit>` are builds of every merge, for trying unreleased changes.
  - Pin a version (`:1.4.0`) rather than `latest` on anything you care about.
  - To build it yourself: `docker build -t vroksnet .` from the repository root.
- **It runs as an unprivileged user**, `app` (UID `1654`), not root. A new named volume is created writable by it; for an existing volume see [Upgrade and rollback](#upgrade-and-rollback).

- `--restart unless-stopped`: the image runs under `tini`, so a crash exits the container (code `134` for an unhandled exception) instead of hanging, and the restart policy brings it back.
- `-v vroksnet-data:/app/data`: without a volume the database lives in the container's writable layer and is lost when the container is recreated.
- `Provider__PublicUrl`: the address the Admin UI tells users to point a service at. Without it the operation card shows `<provider host>:7353`.
- Database migrations run automatically on startup. There is no separate migration step.

Check it's up:

```bash
curl -s http://<host>:8080/health               # → Healthy
curl -s http://<host>:8080/api/system/storage   # → {"isInMemory":false,"filePath":"/app/data/vroksnet.db"}
curl -s http://<host>:8080/api/system/provider  # → {"enabled":true,"port":7353,"publicUrl":…,"corsOrigins":[]}
```

**Health checks** (port `8080`, plain-text body, no details):

| Endpoint | Checks | Use it for |
|---|---|---|
| `GET /alive` | The process answers requests | Liveness: restart the container when it fails |
| `GET /health` | `/alive`, plus the SQLite database exists and opens | Readiness and monitoring. `503 Unhealthy` usually means a missing or unreadable database file or volume, which a restart won't fix |

Brokers aren't part of either: VroksNet doesn't depend on any broker, only on the connections users create. For a Docker health check, note that the `aspnet` image has no `curl`, so probe from outside the container (or from your orchestrator).

TLS isn't terminated by the container. If you need HTTPS, put a reverse proxy in front of port `8080`.

## Configuration

All settings are environment variables (or the matching keys in `appsettings.json`; `__` becomes `:`).

| Variable | Default in the image | Effect |
|---|---|---|
| `ConnectionStrings__VroksNetDb` | `Data Source=/app/data/vroksnet.db` | SQLite connection string. **Unset or empty = in-memory database**, wiped on every restart (the Settings page then shows a warning). Missing parent directories are created |
| `Provider__Port` | `7353` | Port for provider mode. Unset turns provider mode off. Must differ from the API's own port, or startup fails |
| `Provider__PublicUrl` | not set | Provider address shown in the Admin UI and `GET /api/system/provider` |
| `Provider__CorsOrigins` | not set (CORS off) | Comma-separated browser origins allowed to call the provider port, or `*`. No credentials are allowed |
| `TestRuns__MaxConcurrency` | `4` | How many background test runs may execute at once. A run of the same scenario never overlaps another |
| `TestRuns__RetentionPerScenario` | `100` | How many finished runs of each scenario the history keeps; older ones are deleted every few minutes |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port for the API and Admin UI |
| `Logging__LogLevel__Default` | `Information` | Log level. EF Core SQL logging is at `Warning` on purpose (the publisher worker queries every second) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | not set | When set, logs, traces and metrics are exported over OTLP |

To move the provider port, change the variable and the published port together, and update the public URL:

```bash
-p 9000:9000 -e Provider__Port=9000 -e Provider__PublicUrl=http://<host>:9000
```

Startup fails on purpose (check `docker logs vroksnet`) when:

- `Provider:Port` isn't between 1 and 65535, or equals one of the API's own ports.
- `Provider:Port` is set together with `Kestrel:Endpoints` configuration. Use one or the other.

## Provisioning

To start an instance already configured — specs imported, connections, Publishers and test scenarios created — mount a directory at `/app/provisioning`:

```
provisioning/
  specs/          OpenAPI / Swagger / AsyncAPI files, any depth; the kind comes from the document itself
  vroksnet.yaml   optional: connections, spec settings, Publishers, test scenarios
```

```bash
docker run -d --name vroksnet -p 8080:8080 -p 7353:7353 \
  -v vroksnet-data:/app/data \
  -v "$PWD/provisioning":/app/provisioning:ro \
  -e ConnectionStrings__orders=amqp://user:pass@rabbit:5672 \
  ghcr.io/versussun/vroksnet:latest
```

- **The manifest** follows `docs/schemas/provisioning-manifest.v1.schema.json` (also in the image at `/app/provisioning-manifest.v1.schema.json`); ADR 0001 has a full example. Everything is referenced by name, specs by their `info.title`.
- **Secrets:** a connection can take its value from configuration instead of the file — `valueFrom: ConnectionStrings:orders` reads the `ConnectionStrings__orders` variable.
- **Every start applies it again.** Specs are re-imported (an update in place), and provisioned objects are brought back in line with the file — edits made to them in the UI are lost. Objects created in the UI aren't touched. Removing something from the manifest doesn't delete it.
- **Readiness:** `/health` answers `503` until provisioning has been applied.
- **Errors stop the container** with exit code `3`, after logging each one with its file or manifest entry (`docker logs vroksnet`). With `-e Provisioning__FailOnError=false` it keeps running with whatever did apply, and `/health` reports `Degraded` (still `200`).
- A different directory: `Provisioning__Path`.

## Backup and restore

The database runs in WAL mode, so `vroksnet.db` alone is not a consistent copy while the app is running. The `-wal` and `-shm` files next to it hold recent writes. Stop the container and copy the whole volume:

```bash
docker stop vroksnet
docker run --rm -v vroksnet-data:/data -v "$PWD":/backup alpine \
  tar czf /backup/vroksnet-$(date +%F).tgz -C /data .
docker start vroksnet
```

Restore:

```bash
docker stop vroksnet
docker run --rm -v vroksnet-data:/data -v "$PWD":/backup alpine \
  sh -c 'rm -rf /data/* && tar xzf /backup/vroksnet-2026-10-02.tgz -C /data'
docker start vroksnet
```

What's in the database: specifications, mock endpoint toggles, connections (including broker credentials in their connection strings), test scenarios, publishers and the call history. Treat backups as secrets because of the credentials.

## Upgrade and rollback

```bash
docker pull ghcr.io/versussun/vroksnet:<new version>
# back up the volume (see above), then:
docker rm -f vroksnet
docker run -d --name vroksnet …   # same arguments as before, with the new tag
```

**Upgrading from an image that ran as root** (any image built before the switch to the `app` user): the volume's files are owned by root, and the new image can't write them. It exits right after starting, with `SQLite Error 8: 'attempt to write a readonly database'` in `docker logs`. Hand the volume to UID `1654` once, with the container stopped:

```bash
docker run --rm -v vroksnet-data:/app/data alpine chown -R 1654:1654 /app/data
```

Pending migrations are applied on startup. Migrations only go forward: to roll back, restore the backup taken before the upgrade and start the previous image. Don't start an older image against a database a newer one has migrated.

## Day-to-day

**Call history grows without limit.** Every mock call, scenario run and publisher publish is recorded, and there's no automatic retention. A busy mock or a publisher at a 1-second interval adds up quickly. Clear it from the Admin UI (**Call History** → **Clear history**) or:

```bash
curl -s -X DELETE http://<host>:8080/api/call-records   # → {"deleted":1234}
```

SQLite reuses the freed space for new records, but the file doesn't shrink on its own.

**Stop all scheduled publishing** (e.g. a broker is being flooded): disable the publishers on the **Publishers** page, or:

```bash
for id in $(curl -s http://<host>:8080/api/publishers | jq -r '.[] | select(.isEnabled) | .id'); do
  curl -s -X PUT http://<host>:8080/api/publishers/$id/enabled \
    -H "Content-Type: application/json" -d '{"enabled":false}'
done
```

Stopping the container also stops publishing. Publishers stay enabled in the database and resume on the next start.

**Start from scratch:** stop the container and delete the volume (`docker volume rm vroksnet-data`). Everything is lost, including connections.

## Logs

`docker logs vroksnet`. With `OTEL_EXPORTER_OTLP_ENDPOINT` set, the same logs also go to your OTLP collector. The messages that matter:

| Message | Level | Meaning |
|---|---|---|
| `Serialized database write failed.` | Error | A database write failed. Every write goes through one writer, so repeated errors mean nothing is being saved. See [Database writes fail](#database-writes-fail) |
| `Publishing the due publishers failed.` | Error | A whole publisher tick failed (usually reading the publishers from the database). The worker retries on the next tick |
| `Publisher {PublisherId} failed to publish.` | Warning | One publisher threw. Failures the broker reports (refused, timeout) aren't logged here; they show on the publisher itself |
| `Couldn't log mock call {Method} {Path} to the call history.` | Warning | The mock answered, but the call wasn't recorded |

## Troubleshooting

### The container keeps restarting

Read the last start: `docker logs --tail 100 vroksnet`. Usual causes are the provider port settings above, and a database that can't be opened (volume not writable, disk full).

### Database writes fail

Saving anything returns `500`, and the log has `Serialized database write failed.` If the file itself is gone or unreadable, `/health` also returns `503 Unhealthy`.

1. Disk space on the host: `df -h`, and the volume's size.
2. The volume is writable by the container's user, UID `1654`. `SQLite Error 8: 'attempt to write a readonly database'` means it isn't — typically a volume created by an older image that ran as root; see [Upgrade and rollback](#upgrade-and-rollback).
3. Only one container uses the volume: `docker ps --filter volume=vroksnet-data`. Two instances on one SQLite file fight over locks (the writer waits up to 5 seconds, then fails) and both publish every publisher.

### Data disappeared after a restart

The app ran with an in-memory database. The **Settings** page shows **In-memory database**, and `GET /api/system/storage` returns `"isInMemory": true`. Set `ConnectionStrings__VroksNetDb` (the image sets it unless it was overridden with an empty value) and mount a volume. Data from the in-memory run can't be recovered.

### A publisher isn't publishing

Check the publisher on the **Publishers** page (or `GET /api/publishers`): `isEnabled`, `lastPublishedAt`, `lastPublishSuccess`, `lastPublishMessage`.

| `lastPublishMessage` | Fix |
|---|---|
| `The publisher's specification, operation, or connection no longer exists.` | One of them was deleted, or the spec re-import dropped the operation. Edit or recreate the publisher |
| `Timed out connecting to the broker after 10s.` | The broker isn't reachable from the container. Use **Test** on the connection in **Settings** |
| `… the message doesn't match the spec (N violation(s)).` | It was published, but the payload breaks the message schema. Fix the payload override or the spec's example |
| empty, `lastPublishedAt` empty | Never ran: the publisher is disabled, or the worker logs `Publishing the due publishers failed.` |

**Publish now** runs the same code path once and shows the result immediately, which is the quickest way to test a fix. Each publish is also in **Call History** as a Broker publish (out).

Timing: the worker checks once a second, and the interval counts from when the previous publish *started*. A publisher is never published twice at the same time, so a broker that takes longer than the interval stretches it.

### A broker connection works from my machine but not from VroksNet

The connection string is resolved inside the container. `localhost` there is the container itself. Use the broker's hostname on the shared Docker network, or `host.docker.internal` for a broker on the host (on Linux, add `--add-host=host.docker.internal:host-gateway` to `docker run`).

### A mock returns 404 or the wrong response

- On port `8080`, mocks are under `/mock/…`. Real paths only work on the provider port.
- On the provider port, an operation answers only if its endpoint is enabled and **Serve at real path** (provider mode) is on for it. See [Type 3](guides/contract-testing/type-3-provider-http.md).
- Re-importing a spec with the same `info.title` updates it in place: an operation still in the spec gets the new example and schemas but keeps its enabled state and **Serve at real path**, while an operation removed from the spec is gone. Examples can't be edited in the UI; change the spec and re-import it.

### A browser app can't call the provider port

CORS is off by default. Set `Provider__CorsOrigins` and restart. `GET /api/system/provider` shows the active list in `corsOrigins`.

## Constraints

- **One instance only.** SQLite is a local file, and the publisher worker has no coordination between instances: two instances would publish every publisher twice. Don't scale out or run two containers on one volume.
- **No authentication.** Keep both ports on an internal network. The provider port listens on all interfaces in the container, like `8080`.
- **Brokers aren't included.** The image has no RabbitMQ, NATS or Kafka. Point connections at your own.

## Local development (Aspire)

```bash
dotnet run --project src/VroksNet.AppHost
```

Needs Docker running (RabbitMQ, NATS and Kafka start as containers) and a trusted dev certificate (`dotnet dev-certs https --trust`).

| Symptom | Cause |
|---|---|
| ApiService doesn't start, address in use | Ports `7352` (API) and `7353` (provider) are fixed, because the Admin UI dev server has no way to discover a random port. Free them |
| Data is gone after every run | No `ConnectionStrings__VroksNetDb` in development means an in-memory database. Set it (e.g. with `dotnet user-secrets` on `VroksNet.ApiService`) to keep data |
| Admin UI can't reach the API (CORS) | In Development the API allows any `localhost` / `*.localhost` origin. For another hostname, add it to `Cors__AdditionalDevOrigins` |

The Aspire dashboard shows logs, traces and the `apiservice` health (from `/health`) for every resource.
