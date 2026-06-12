#!/usr/bin/env bash
# Lint the generated OpenAPI spec against the AEP OpenAPI ruleset (Spectral).
# Boots the server, dumps /openapi.json, and fails on any error or warning that isn't
# an explicitly triaged deviation in .spectral.yaml.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_lib.sh"

trap stop_server EXIT

if [ ! -d "$SCRIPT_DIR/node_modules" ]; then
  log "Installing linter deps (npm ci)…"
  (cd "$SCRIPT_DIR" && npm ci)
fi

# Allow linting a pre-dumped spec (SPEC_FILE set + file exists) without booting the server.
if [ -n "${REUSE_SPEC:-}" ] && [ -f "$SPEC_FILE" ]; then
  log "Reusing existing spec at $SPEC_FILE"
else
  build_server
  start_server
fi

log "Running Spectral (fail on warn or error)…"
cd "$SCRIPT_DIR"
npx --no-install spectral lint --fail-severity=warn --ruleset .spectral.yaml "$SPEC_FILE"
ok "OpenAPI spec is AEP-conformant (modulo triaged deviations in .spectral.yaml)"
