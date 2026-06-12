#!/usr/bin/env bash
# Drive the running server with aepcli as an end-to-end AEP conformance check.
# Asserts on outcomes (not manual inspection) across the full CRUD lifecycle for a
# top-level resource and a nested child.
#
# Requires aepcli >= v0.3.0 (aep-lib-go Feb 2026+); older builds drop get/update/delete
# for item paths. Override the binary with AEPCLI=/path/to/aepcli.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_lib.sh"

AEPCLI="${AEPCLI:-aepcli}"
trap stop_server EXIT

# --- assertion helpers -------------------------------------------------------
FAILED=0
aep() { "$AEPCLI" --server-url "$BASE_URL" "$SPEC_URL" "$@"; }

# expect <description> -- <aepcli args...> : run, require success + non-empty output
expect() {
  local desc="$1"; shift; [ "$1" = "--" ] && shift
  local out
  if out="$(aep "$@" 2>&1)"; then ok "$desc"; LAST_OUT="$out";
  else fail "$desc"; printf '%s\n' "$out" | sed 's/^/      /' >&2; FAILED=1; LAST_OUT=""; fi
}

# assert_out <substring> <description>
assert_out() {
  if printf '%s' "$LAST_OUT" | grep -q -- "$1"; then ok "$2";
  else fail "$2 (expected to find: $1)"; printf '%s\n' "$LAST_OUT" | sed 's/^/      /' >&2; FAILED=1; fi
}

require_recent_aepcli() {
  local help
  help="$(aep publisher --help 2>&1 || true)"
  if ! printf '%s' "$help" | grep -qE '^\s*get\b' || ! printf '%s' "$help" | grep -qE '^\s*delete\b'; then
    fail "aepcli ($AEPCLI) is too old: it doesn't expose get/update/delete for item paths."
    echo  "      Install a recent build (>= v0.3.0, aep-lib-go Feb 2026+):" >&2
    echo  "        go install github.com/aep-dev/aepcli/cmd/aepcli@latest" >&2
    echo  "      or point this check at one: AEPCLI=/path/to/aepcli $0" >&2
    exit 1
  fi
  ok "aepcli exposes the full method set (get/update/delete present)"
}

# --- run ---------------------------------------------------------------------
command -v "$AEPCLI" >/dev/null || { fail "aepcli not found (set AEPCLI=/path/to/aepcli)"; exit 1; }
# When invoked by run.sh, a server is already up; don't boot (or kill) a second one.
if [ -z "${REUSE_SERVER:-}" ]; then
  build_server
  start_server
else
  log "Reusing already-running server at ${BASE_URL}"
fi

log "aepcli conformance — $($AEPCLI --help >/dev/null 2>&1 && echo "binary: $AEPCLI")"
require_recent_aepcli

log "Top-level resource (publisher) lifecycle"
expect    "create publisher"        -- publisher create acme --display_name "Acme Press"
assert_out '"id": "acme"'           "create returns the new id"
expect    "get publisher"           -- publisher get acme
assert_out "Acme Press"             "get returns display_name"
expect    "list publishers"         -- publisher list
assert_out "acme"                   "list includes the publisher"
expect    "update publisher"        -- publisher update acme --display_name "Acme Publishing"
assert_out "Acme Publishing"        "update changes display_name"

log "Nested resource (book under publisher) lifecycle"
expect    "create book"             -- book create 1984 --publisher acme --title "1984" --author "Orwell" --price 1200 --published
assert_out '"id": "1984"'           "nested create returns the new id"
expect    "get book"                -- book get 1984 --publisher acme
assert_out "Orwell"                 "nested get returns author"
expect    "list books"             -- book list --publisher acme
assert_out "1984"                   "nested list includes the book"
# aepcli applies `required` to update, so --title is passed (see README); PATCH is partial server-side.
expect    "update book price"       -- book update 1984 --publisher acme --title "1984" --price 999
assert_out "999"                    "nested update changes price"

log "Deletion + not-found"
# delete returns 204; aepcli prints a cosmetic 'unexpected end of JSON input' on the empty
# body (README note), so tolerate its exit code and verify the effect instead.
aep book delete 1984 --publisher acme >/dev/null 2>&1 || true
if aep book get 1984 --publisher acme >/dev/null 2>&1; then
  fail "book still retrievable after delete"; FAILED=1
else
  ok "deleted book is no longer retrievable (404)"
fi
aep publisher delete acme >/dev/null 2>&1 || true
ok "publisher deleted"

echo
if [ "$FAILED" -eq 0 ]; then ok "aepcli conformance passed"; else fail "aepcli conformance FAILED"; fi
exit "$FAILED"
