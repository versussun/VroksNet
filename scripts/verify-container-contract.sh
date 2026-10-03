#!/usr/bin/env bash
# Verifies a built image against docs/container-contract.md (§9) — what the Aspire hosting
# package relies on. CI runs it on every image build, before anything is published; run it
# locally the same way:
#
#   docker build -t vroksnet:local . && scripts/verify-container-contract.sh vroksnet:local
#
# Needs docker, curl and jq. Exits non-zero on the first broken item, with the container's log.
set -euo pipefail

IMAGE="${1:?usage: $0 <image>}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_ID="vroksnet-contract-$$"
VOLUME="$RUN_ID-data"
STARTUP_TIMEOUT=90
EXIT_TIMEOUT=30

# The contract version the code implements; the label and /api/system/info must both report it.
CONTRACT_VERSION="$(sed -nE 's/.*const int Version = ([0-9]+);.*/\1/p' "$ROOT/src/VroksNet.Application/System/ContainerContract.cs")"
[[ -n "$CONTRACT_VERSION" ]] || { echo "Couldn't read ContainerContract.Version" >&2; exit 1; }

# Every top-level sample spec, mounted file by file (as the package does), plus the sample manifest.
SPEC_MOUNTS=()
SPEC_COUNT=0
for spec in "$ROOT"/docs/samples/*.yaml; do
  SPEC_MOUNTS+=(-v "$spec:/app/provisioning/specs/$(basename "$spec"):ro")
  SPEC_COUNT=$((SPEC_COUNT + 1))
done
MANIFEST_MOUNT=(-v "$ROOT/docs/samples/provisioning/vroksnet.yaml:/app/provisioning/vroksnet.yaml:ro")
KAFKA_ENV=(-e Provisioning__Connections__0__Name=kafka -e Provisioning__Connections__0__Type=Kafka -e Provisioning__Connections__0__Value=kafka:9092)

BROKEN_DIR="$(mktemp -d)"
CURRENT=""

cleanup() {
  docker ps -aq --filter "name=^$RUN_ID" | xargs -r docker rm -f >/dev/null 2>&1 || true
  docker volume rm -f "$VOLUME" >/dev/null 2>&1 || true
  rm -rf "$BROKEN_DIR"
}
trap cleanup EXIT

fail() {
  echo "✗ $*" >&2
  if [[ -n "$CURRENT" ]]; then
    echo "--- docker logs $CURRENT ---" >&2
    docker logs "$CURRENT" 2>&1 | tail -60 >&2 || true
  fi
  exit 1
}

pass() { echo "✓ $*"; }

expect_eq() { # <what> <actual> <expected>
  [[ "$2" == "$3" ]] || fail "$1: expected '$3', got '$2'"
}

# Starts a detached container named $RUN_ID-<suffix>, publishing 8080 on a random local port.
start() { # <suffix> <docker run args...>
  CURRENT="$RUN_ID-$1"
  shift
  docker run -d --name "$CURRENT" -p 127.0.0.1::8080 "$@" "$IMAGE" >/dev/null
}

base_url() { echo "http://$(docker port "$CURRENT" 8080/tcp | head -1)"; }

wait_healthy() {
  local url code
  url="$(base_url)"
  for _ in $(seq 1 "$STARTUP_TIMEOUT"); do
    code="$(curl -s -o /dev/null -w '%{http_code}' "$url/health" || true)"
    [[ "$code" == 200 ]] && return 0
    [[ "$(docker inspect -f '{{.State.Running}}' "$CURRENT")" == true ]] || fail "$CURRENT exited before /health was 200"
    sleep 1
  done
  fail "/health wasn't 200 within ${STARTUP_TIMEOUT}s (last: $code)"
}

wait_exit() { # sets EXIT_CODE
  for _ in $(seq 1 "$EXIT_TIMEOUT"); do
    if [[ "$(docker inspect -f '{{.State.Running}}' "$CURRENT")" != true ]]; then
      EXIT_CODE="$(docker inspect -f '{{.State.ExitCode}}' "$CURRENT")"
      return 0
    fi
    sleep 1
  done
  fail "$CURRENT still running after ${EXIT_TIMEOUT}s (expected it to stop)"
}

echo "Verifying $IMAGE against container contract v$CONTRACT_VERSION"

# --- §2/§3: image metadata ---
inspect() { docker image inspect -f "$1" "$IMAGE"; }
expect_eq "label io.vroksnet.contract.version" "$(inspect '{{index .Config.Labels "io.vroksnet.contract.version"}}')" "$CONTRACT_VERSION"
[[ -n "$(inspect '{{index .Config.Labels "org.opencontainers.image.version"}}')" ]] || fail "label org.opencontainers.image.version is empty"
[[ -n "$(inspect '{{index .Config.Labels "org.opencontainers.image.source"}}')" ]] || fail "label org.opencontainers.image.source is empty"
expect_eq "user" "$(inspect '{{.Config.User}}')" "1654"
expect_eq "entry point" "$(inspect '{{json .Config.Entrypoint}}')" '["/usr/bin/tini","--","dotnet","VroksNet.ApiService.dll"]'
expect_eq "exposed ports" "$(inspect '{{range $p, $_ := .Config.ExposedPorts}}{{$p}} {{end}}')" "7353/tcp 8080/tcp "
expect_eq "volumes" "$(inspect '{{range $v, $_ := .Config.Volumes}}{{$v}} {{end}}')" "/app/data "
docker run --rm --entrypoint cat "$IMAGE" /app/provisioning-manifest.v1.schema.json \
  | cmp -s - "$ROOT/docs/schemas/provisioning-manifest.v1.schema.json" \
  || fail "/app/provisioning-manifest.v1.schema.json differs from docs/schemas/"
pass "image: labels, user, entry point, ports, volume, schema"

# --- §4–§6: provisioned from files + a variable ---
start provisioned -v "$VOLUME:/app/data" "${SPEC_MOUNTS[@]}" "${MANIFEST_MOUNT[@]}" "${KAFKA_ENV[@]}"
wait_healthy
URL="$(base_url)"
expect_eq "/alive" "$(curl -s -o /dev/null -w '%{http_code}' "$URL/alive")" 200
INFO="$(curl -sf "$URL/api/system/info")" || fail "GET /api/system/info failed"
expect_eq "contractVersion" "$(jq -r .contractVersion <<<"$INFO")" "$CONTRACT_VERSION"
[[ "$(jq -r '.version // ""' <<<"$INFO")" != "" ]] || fail "version is empty"
expect_eq "provisioning.status" "$(jq -r .provisioning.status <<<"$INFO")" Applied
expect_eq "provisioning.source" "$(jq -r .provisioning.source <<<"$INFO")" /app/provisioning
expect_eq "provisioning.counts" "$(jq -c .provisioning.counts <<<"$INFO")" \
  "{\"specifications\":$SPEC_COUNT,\"connections\":2,\"publishers\":1,\"testScenarios\":2,\"testSuites\":1}"
expect_eq "provisioning.errors" "$(jq -c .provisioning.errors <<<"$INFO")" "[]"
[[ "$(jq -r '.provisioning.appliedAt // ""' <<<"$INFO")" != "" ]] || fail "appliedAt is empty"
pass "provisioned from $SPEC_COUNT specs, the manifest and Provisioning__Connections__0: /health, /alive, /api/system/info"

# The manifest's runOnStartup suite runs after provisioning, without holding up /health.
for _ in $(seq 1 "$STARTUP_TIMEOUT"); do
  SUITE_STATUS="$(curl -s "$URL/api/test-suites/smoke/runs/latest" | jq -r '.status // empty')"
  [[ "$SUITE_STATUS" =~ ^(Passed|Failed|Cancelled|Interrupted)$ ]] && break
  sleep 1
done
expect_eq "startup suite \"smoke\"" "$SUITE_STATUS" Passed
pass "runOnStartup suite ran and passed"

# --- §7: SIGTERM is a clean exit ---
docker stop -t 30 "$CURRENT" >/dev/null
expect_eq "exit code after docker stop" "$(docker inspect -f '{{.State.ExitCode}}' "$CURRENT")" 0
pass "SIGTERM → exit code 0"

# --- §7: starting again on the same volume creates no duplicates ---
start restarted -v "$VOLUME:/app/data" "${SPEC_MOUNTS[@]}" "${MANIFEST_MOUNT[@]}" "${KAFKA_ENV[@]}"
wait_healthy
URL="$(base_url)"
expect_eq "specifications after restart" "$(curl -sf "$URL/api/specifications" | jq length)" "$SPEC_COUNT"
expect_eq "connections after restart" "$(curl -sf "$URL/api/connections" | jq length)" 2
expect_eq "test scenarios after restart" "$(curl -sf "$URL/api/test-scenarios" | jq length)" 2
pass "restart on the same volume: no duplicates"
docker rm -f "$CURRENT" >/dev/null

# --- §5: variables alone, no directory ---
start variables-only \
  -e Provisioning__Connections__0__Name=orders -e Provisioning__Connections__0__Type=RabbitMq \
  -e Provisioning__Connections__0__ValueFrom=ConnectionStrings:orders -e ConnectionStrings__orders=amqp://guest:guest@rabbit:5672
wait_healthy
INFO="$(curl -sf "$(base_url)/api/system/info")" || fail "GET /api/system/info failed"
expect_eq "provisioning.status (variables only)" "$(jq -r .provisioning.status <<<"$INFO")" Applied
expect_eq "provisioning.source (variables only)" "$(jq -r .provisioning.source <<<"$INFO")" configuration
expect_eq "provisioning.counts.connections (variables only)" "$(jq -r .provisioning.counts.connections <<<"$INFO")" 1
pass "provisioned from variables alone"
docker rm -f "$CURRENT" >/dev/null

# --- §6: nothing to provision ---
start unconfigured
wait_healthy
expect_eq "provisioning.status (nothing mounted)" "$(curl -sf "$(base_url)/api/system/info" | jq -r .provisioning.status)" NotConfigured
pass "nothing mounted → NotConfigured, Healthy"
docker rm -f "$CURRENT" >/dev/null

# --- §7: a broken spec stops the container with exit code 3 ---
mkdir -p "$BROKEN_DIR/specs"
echo "title: not a spec" >"$BROKEN_DIR/specs/broken.yaml"
chmod -R a+rX "$BROKEN_DIR"
start broken-spec -v "$BROKEN_DIR:/app/provisioning:ro"
wait_exit
expect_eq "exit code with a broken spec" "$EXIT_CODE" 3
docker logs "$CURRENT" 2>&1 | grep -q "specs/broken.yaml" || fail "the log doesn't name specs/broken.yaml"
pass "broken spec → exit code 3, logged with its file"

# --- §5: the same connection name in the manifest and in a variable is an error ---
start duplicate-connection "${MANIFEST_MOUNT[@]}" -e Provisioning__Connections__0__Name=bookstore-http \
  -e Provisioning__Connections__0__Type=Http -e Provisioning__Connections__0__Value=http://elsewhere
wait_exit
expect_eq "exit code with a duplicate connection" "$EXIT_CODE" 3
pass "a connection named in both the manifest and a variable → exit code 3"

CURRENT=""
echo "Container contract v$CONTRACT_VERSION: all checks passed."
