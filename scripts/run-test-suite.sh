#!/usr/bin/env bash
# Runs a VroksNet test suite and waits for its result — the CI step (ADR 0002, "Suites"):
#
#   scripts/run-test-suite.sh http://localhost:8080 payments-contract [timeout-seconds]
#
# The suite is addressed by name (or id). Prints each scenario's result and exits:
#   0  the suite passed
#   1  it failed, was cancelled or interrupted (the failing scenarios are listed)
#   2  it couldn't be run: unknown suite, VroksNet unreachable, or no result within the timeout
# Needs curl and jq.
set -euo pipefail

BASE_URL="${1:?usage: $0 <vroksnet-url> <suite-name-or-id> [timeout-seconds]}"
SUITE="${2:?usage: $0 <vroksnet-url> <suite-name-or-id> [timeout-seconds]}"
TIMEOUT="${3:-1800}"
BASE_URL="${BASE_URL%/}"
SUITE_PATH="$(jq -rn --arg suite "$SUITE" '$suite | @uri')"

response="$(curl -sS -X POST -w '\n%{http_code}' "$BASE_URL/api/test-suites/$SUITE_PATH/runs")" || { echo "Couldn't reach VroksNet at $BASE_URL." >&2; exit 2; }
code="${response##*$'\n'}"
body="${response%$'\n'*}"
case "$code" in
  202) ;;
  404) echo "No test suite \"$SUITE\" in VroksNet at $BASE_URL." >&2; exit 2 ;;
  *) echo "Starting the suite failed (HTTP $code): $body" >&2; exit 2 ;;
esac

RUN_ID="$(jq -r .suiteRunId <<<"$body")"
echo "Started suite \"$SUITE\" (run $RUN_ID); waiting up to ${TIMEOUT}s."

deadline=$((SECONDS + TIMEOUT))
while true; do
  run="$(curl -sS -f "$BASE_URL/api/suite-runs/$RUN_ID")" || { echo "Couldn't read suite run $RUN_ID." >&2; exit 2; }
  status="$(jq -r .status <<<"$run")"
  case "$status" in
    Passed|Failed|Cancelled|Interrupted) break ;;
  esac
  if ((SECONDS >= deadline)); then
    echo "Suite run $RUN_ID is still $status after ${TIMEOUT}s." >&2
    exit 2
  fi
  sleep 2
done

jq -r '.runs[] | "  \(if .status == "Passed" then "✓" else "✗" end) \(.scenarioName): \(.status)\(if .message then " — \(.message)" else "" end)"' <<<"$run"
echo "$status: $(jq -r '.message // ""' <<<"$run")"

[[ "$status" == Passed ]]
