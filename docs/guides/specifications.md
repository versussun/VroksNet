# Specification folders

A **specification** is an OpenAPI / Swagger, AsyncAPI or `.proto` file that VroksNet imports and splits into operations: mocks, Publishers and test scenarios all start from one.

There are two ways to get specs in:

| Way | Kept across restarts | Good for |
|---|---|---|
| Upload in the Admin UI or through the API | yes (in the database) | trying things out, one spec at a time. See [Getting started → Import a specification](contract-testing/getting-started.md#import-a-specification) |
| [A folder mounted at startup](#one-folder) | re-imported at every start | a reproducible setup: a folder (or several) of specs kept in Git, Compose, Aspire, CI |

This guide covers the second way. It's part of **provisioning**, the same mechanism that creates [connections](connections.md), Publishers and test scenarios from `vroksnet.yaml`.

## The layout

VroksNet reads the provisioning root, `/app/provisioning` by default:

```
/app/provisioning/
  specs/          any number of spec files, nested folders at any depth
  vroksnet.yaml   optional: connections, spec settings, Publishers, test scenarios, test suites
```

Inside `specs/`:

- **Read:** `*.yaml`, `*.yml`, `*.json` and `*.proto`, in every subfolder. Other files (`README.md`, `.txt`, images) are skipped.
- **The kind comes from the document, not the file name.** A root key `openapi` or `swagger` makes it OpenAPI, `asyncapi` makes it AsyncAPI.
- **Every `.yaml`/`.yml`/`.json` must be a spec.** One without any of those root keys (a `package.json`, a Helm values file, a JSON example) is an error that stops the container, so keep such files out of the mounted folder.
- **Files are imported in path order,** so runs are repeatable.
- **A spec is identified by its title** (`info.title`, or the `package` and services for `.proto`), not by the file name or folder. Re-importing it updates it in place: operations still in the spec keep their ids, enabled state and provider mode.

`specs/` and `vroksnet.yaml` are independent. Specs alone are enough, and so is a manifest alone.

## One folder

Mount your specs folder at `/app/provisioning/specs`, read-only:

```bash
docker run -d --name vroksnet -p 8080:8080 -p 7353:7353 -p 7354:7354 \
  -v vroksnet-data:/app/data \
  -v "$PWD/specs":/app/provisioning/specs:ro \
  ghcr.io/versussun/vroksnet:latest
```

In PowerShell, write `"${PWD}/specs"` instead of `"$PWD/specs"`.

If you also have a manifest, mount the whole provisioning folder instead (`specs/` plus `vroksnet.yaml`, the layout **Export** produces):

```bash
  -v "$PWD/provisioning":/app/provisioning:ro
```

## Several folders

Mount each folder **into its own subfolder** of `specs/`. Nested folders are read like any others:

```bash
docker run -d --name vroksnet -p 8080:8080 -p 7353:7353 -p 7354:7354 \
  -v vroksnet-data:/app/data \
  -v "$PWD/../orders-service/api":/app/provisioning/specs/orders:ro \
  -v "$PWD/../payments-service/contracts":/app/provisioning/specs/payments:ro \
  -v "$PWD/shared-events":/app/provisioning/specs/events:ro \
  -v "$PWD/vroksnet.yaml":/app/provisioning/vroksnet.yaml:ro \
  ghcr.io/versussun/vroksnet:latest
```

- **Don't mount two folders at the same path.** A second mount at `/app/provisioning/specs` hides the first: give each its own subfolder.
- Single files mount the same way: `-v "$PWD/billing.yaml":/app/provisioning/specs/billing.yaml:ro`. The [provisioning sample](../samples/provisioning/README.md) mounts its specs file by file.
- **Titles must be unique across all folders.** Two files with the same `info.title` don't make two specs: the one later in path order silently replaces the other.

## Docker Compose

```yaml
services:
  vroksnet:
    image: ghcr.io/versussun/vroksnet:latest
    ports: ["8080:8080", "7353:7353", "7354:7354"]
    volumes:
      - vroksnet-data:/app/data
      - ./specs:/app/provisioning/specs:ro                              # one folder
      - ../payments-service/contracts:/app/provisioning/specs/payments:ro  # another one, in its own subfolder
      - ./vroksnet.yaml:/app/provisioning/vroksnet.yaml:ro                # optional

volumes:
  vroksnet-data:
```

Relative paths in Compose are relative to the `compose.yaml` file. `/health` answers `503` until provisioning has been applied, so tests can poll it before calling the mocks. The image has no `curl` or `wget` for a Compose `healthcheck`.

## A different root

`Provisioning__Path` moves the provisioning root, and `specs/` is read under it:

```bash
  -v "$PWD/contracts":/contracts:ro \
  -e Provisioning__Path=/contracts
```

## .NET Aspire

The Aspire hosting package (a separate repository) mounts folders the same way: `WithSpecifications(dir)` for a folder of specs, `WithSpecification(file)` for one file, `WithProvisioningDirectory(dir)` for a whole provisioning folder. The full mapping is in the [image contract §8](../container-contract.md#8-how-the-aspire-package-uses-this-for-reference).

## `.proto` files

- **All `.proto` files are parsed together,** across every folder under `specs/`, because they import each other.
- An `import` names a file **by its path under `specs/`**. When nothing is at that exact path, the only file whose path ends with it is used: with `common/money.proto` mounted under `specs/grpc/`, `import "common/money.proto";` still finds `specs/grpc/common/money.proto`. If two folders both have a `common/money.proto`, the import is ambiguous and reported missing, so give such files distinct paths.
- Each file that declares a `service` becomes a specification. A file with only messages (like `money.proto`) is just an import.
- **One broken `.proto` stops them all:** the error names its file and line, and no `.proto` is imported. OpenAPI and AsyncAPI files are unaffected.
- A runnable example: [`docs/samples/grpc/`](../samples/README.md). Keep the folder together, because its imports name files by their path inside it.

## Settings for a spec

What the files can't say goes into `vroksnet.yaml`, referring to the spec by its title:

```yaml
version: 1

specifications:
  - title: Bookstore Sample API        # info.title of a file under specs/
    providerMode: true                 # serve its operations at their real paths on the provider port
    disabledOperations: ["DELETE /books/{bookId}"]
```

Publishers and test scenarios refer to specs by title the same way. The schema is [`docs/schemas/provisioning-manifest.v1.schema.json`](../schemas/provisioning-manifest.v1.schema.json). A full example: [`docs/samples/provisioning/vroksnet.yaml`](../samples/provisioning/vroksnet.yaml).

## Adding or changing a spec

**Specs are read only at startup.** VroksNet doesn't watch the folder: after adding, changing or removing a file, restart the container (`docker restart vroksnet`).

- **Every start imports the folder again.** Changed specs are updated in place. Operations still in the spec keep their enabled state and provider mode, including what was set in the UI.
- **A spec listed under `specifications:` in the manifest** is brought back in line with it at every start. UI changes to its enabled operations and provider mode last until the next start.
- **Removing a file doesn't delete its spec.** Delete it in the UI (**Specifications**) if it's no longer needed.
- **Specs uploaded in the UI aren't touched,** unless a file in the folder has the same title. Then the file takes it over, and it becomes provisioned.
- In the Admin UI, provisioned specs carry a **Provisioned** badge.

## When something's wrong

**Errors stop the container** with exit code `3`, after logging each one with its file (`docker logs vroksnet`). With `-e Provisioning__FailOnError=false` it keeps running with whatever did apply, and `/health` reports `Degraded` (still `200`).

Check what was imported (after a successful start, or with `FailOnError=false`):

```bash
curl -s localhost:8080/api/system/info
```

```json
{
  "provisioning": {
    "status": "Failed",
    "source": "/app/provisioning",
    "counts": { "specifications": 4, "connections": 0, "publishers": 0, "testScenarios": 0, "testSuites": 0 },
    "errors": [
      { "source": "specs/payments/values.yaml", "message": "Not an OpenAPI or AsyncAPI document: no openapi, swagger or asyncapi key at its root." }
    ]
  }
}
```

| Symptom | Cause |
|---|---|
| `Not an OpenAPI or AsyncAPI document` | a `.yaml`/`.json` in the folder that isn't a spec. Move it out of the mounted folder |
| A spec is missing, `counts.specifications` is lower than the number of files | two files share a title, so one replaced the other. Or a `.proto` without a `service`, which is only an import |
| `status` is `NotConfigured` | nothing is at `/app/provisioning`: check the mount target, or `Provisioning__Path` |
| `status` is `Applied` but `counts.specifications` is `0` | the folder is mounted, but not at `specs/` under the root (e.g. at `/app/provisioning` itself) |
| A `.proto` import is reported missing | the imported file isn't under `specs/`, or two files fit the path. See [`.proto` files](#proto-files) |
| A new file doesn't show up | the container wasn't restarted |

## From the UI to a folder

Upload and set up specs in the UI, then **Settings** → **Export** (or `GET /api/provisioning/export`) downloads `vroksnet-provisioning.zip`: `specs/` with one file per spec (a `.proto` spec with the files it imports, at their paths) and a `vroksnet.yaml`. Unpack it and mount it at `/app/provisioning`.
