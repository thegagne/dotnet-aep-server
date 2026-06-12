#!/usr/bin/env bash
# Run the full AEP conformance suite: OpenAPI lint (Spectral) + aepcli end-to-end.
# One server boot is shared across both checks.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_lib.sh"

trap stop_server EXIT

if [ ! -d "$SCRIPT_DIR/node_modules" ]; then
  log "Installing linter deps (npm ci)…"
  (cd "$SCRIPT_DIR" && npm ci)
fi

build_server
start_server

RC=0

log "[1/2] OpenAPI lint (Spectral)"
if (cd "$SCRIPT_DIR" && npx --no-install spectral lint --fail-severity=warn --ruleset .spectral.yaml "$SPEC_FILE"); then
  ok "OpenAPI lint passed"
else
  fail "OpenAPI lint failed"; RC=1
fi

echo
log "[2/2] aepcli end-to-end"
# Reuse the already-running server; aepcli script's own start is skipped via REUSE flag.
if REUSE_SERVER=1 SKIP_BUILD=1 bash "$SCRIPT_DIR/aepcli-conformance.sh"; then
  ok "aepcli conformance passed"
else
  fail "aepcli conformance failed"; RC=1
fi

echo
[ "$RC" -eq 0 ] && ok "ALL conformance checks passed" || fail "conformance checks FAILED"
exit "$RC"
