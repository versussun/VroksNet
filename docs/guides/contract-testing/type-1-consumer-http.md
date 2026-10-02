# Type 1: Consumer test (HTTP request → validate the response)

[← Contract testing guides](README.md)

**Checks:** that a service's HTTP responses match its OpenAPI spec.
**How:** VroksNet sends the operation's request to the service and validates the response body against the schema the spec declares **for the status code the service actually returned**.

```
VroksNet ──── GET /pets/7 ────▶ your service
         ◀─── 200 {…}  ──────  (checked against the spec's "200" schema)
```

## 1. The spec

Responses need a JSON schema to be checkable. Declare every status the service may return, including errors:

```yaml
openapi: 3.0.3
info:
  title: Pets API
  version: "1.0.0"
paths:
  /pets/{petId}:
    get:
      responses:
        "200":
          description: The pet
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/Pet"
        "404":
          description: No such pet
          content:
            application/json:
              schema:
                type: object
                required: [message]
                properties:
                  message: { type: string }
        default:
          description: Anything else
components:
  schemas:
    Pet:
      type: object
      required: [id, name]
      properties:
        id: { type: integer }
        name: { type: string }
```

## 2. Import it and create an HTTP connection

See [Getting started](getting-started.md#import-a-specification). The connection's URL is the service's base URL. A base path is kept: with `https://pets.internal/api/v1` and `GET /pets/{petId}`, VroksNet calls `https://pets.internal/api/v1/pets/{petId}`.

> Path parameters are sent literally (`/pets/{petId}`) for now. If the service needs a real id, add an operation with a concrete path to the spec you import for testing, or test an endpoint without parameters.

## 3. Create the scenario

**UI:** **Test Scenarios** → **+ Add test scenario**:

| Field | Value |
|---|---|
| Name | `Get a pet` |
| Specification | `Pets API (OpenApi)` |
| Operation | `GET /pets/{petId}` |
| Connection | your HTTP connection (only HTTP connections are offered for HTTP operations) |
| Payload (optional override) | leave blank to send the spec's example body; fill in to send your own JSON |

→ **Add**.

Shortcut: on a specification's page, **+ Create test** on an operation card opens this form with the spec and operation already picked.

**API:**

```bash
SPEC=$(curl -s -X POST "$API/api/specifications/openapi" -H "Content-Type: text/plain" --data-binary @pets.yaml | jq -r .id)
OP=$(curl -s "$API/api/specifications/$SPEC" | jq -r '.endpoints[] | select(.operationKey=="GET /pets/{petId}") | .id')
CONN=$(curl -s -X POST "$API/api/connections" -H "Content-Type: application/json" \
  -d '{"name":"Pets service","serviceType":"Http","value":"https://pets.internal"}' | jq -r .id)

SCENARIO=$(curl -s -X POST "$API/api/test-scenarios" -H "Content-Type: application/json" -d "{
  \"name\": \"Get a pet\",
  \"specificationId\": \"$SPEC\",
  \"mockEndpointId\": \"$OP\",
  \"connectionId\": \"$CONN\",
  \"payloadOverride\": null
}" | jq -r .id)
```

## 4. Run it

**UI:** **Run** on the scenario's row. The result appears under the buttons: an **OK / Failed** badge, the HTTP status, the response body, and a **Matches spec / Contract violated** badge with the list of violations.

**API:**

```bash
curl -s -X POST "$API/api/test-scenarios/$SCENARIO/run"
```

Passing run:

```json
{
  "success": true,
  "message": "200 OK",
  "responseBody": "{\"id\":7,\"name\":\"Fido\"}",
  "statusCode": 200,
  "contractValidation": { "isValid": true, "errors": [] }
}
```

The service answered, but not as the spec says:

```json
{
  "success": false,
  "message": "200 OK — response doesn't match the spec (1 violation(s)).",
  "responseBody": "{\"id\":\"7\",\"name\":\"Fido\"}",
  "statusCode": 200,
  "contractValidation": {
    "isValid": false,
    "errors": ["Value is \"string\" but should be \"integer\""]
  }
}
```

## How the response is judged

| The service returns… | Result |
|---|---|
| a status declared in the spec, body matches its schema | ✅ passes |
| a status declared in the spec, body doesn't match | ❌ contract violated (errors listed) |
| a status declared **without** a JSON body (e.g. `204`) | ✅ passes, body not checked |
| a status the spec declares a schema for, but an **empty** body | ❌ contract violated |
| a status **not declared** at all (no exact code, no `4XX` range, no `default`) | ❌ contract violated: `Status 500 isn't declared…` |
| no response (timeout, connection refused) | ❌ failed, nothing to validate (`contractValidation` is `null`) |

The schema is picked by the status code: exact code (`404`) first, then its range (`4XX`), then `default`. A run **passes only if** the request went through **and** the response matches.

## Good to know

- **Re-import specs after upgrading VroksNet.** Specs imported before per-status schemas were stored have nothing to validate against, so their runs report `contractValidation: null`.
- **Recursive schemas** (a tree of nodes, a linked list) are supported.
- Every run is logged in [Call History](call-history.md) as *HTTP request (out)*.
