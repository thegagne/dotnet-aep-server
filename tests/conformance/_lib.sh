#!/usr/bin/env bash
# Shared helpers for the conformance checks: build, boot, and tear down the AepServer host.
# Sourced by lint-openapi.sh, aepcli-conformance.sh, and run.sh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

PORT="${AEP_CONFORMANCE_PORT:-5288}"
BASE_URL="http://localhost:${PORT}"
SPEC_URL="${BASE_URL}/openapi.json"
SPEC_FILE="${SPEC_FILE:-/tmp/aep-conformance-openapi.json}"
SERVER_LOG="${SERVER_LOG:-/tmp/aep-conformance-server.log}"
SERVER_PID=""

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m  ✗ %s\033[0m\n' "$*" >&2; }

build_server() {
  log "Building Aep.Server (Release)…"
  dotnet build "$REPO_ROOT/src/Aep.Server" -c Release >/dev/null
}

# Boots the host on an isolated in-memory store (no leftover aep.db, no external deps) and
# blocks until /openapi.json responds, dumping it to $SPEC_FILE.
start_server() {
  log "Starting server on ${BASE_URL} (in-memory store)…"
  Storage__Provider=inmemory \
    dotnet run --project "$REPO_ROOT/src/Aep.Server" -c Release --no-build --no-launch-profile \
    -- --urls "$BASE_URL" >"$SERVER_LOG" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 60); do
    if curl -sf "$SPEC_URL" -o "$SPEC_FILE" 2>/dev/null; then
      ok "Server up; spec saved to $SPEC_FILE"
      return 0
    fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
      fail "Server process exited during startup"; tail -20 "$SERVER_LOG" >&2; return 1
    fi
    sleep 0.5
  done
  fail "Server did not become ready within 30s"; tail -20 "$SERVER_LOG" >&2; return 1
}

stop_server() {
  if [ -n "${SERVER_PID}" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
