# AEP conformance checks

External, AEP-aware conformance for the server — beyond the unit/integration tests. Two checks:

1. **OpenAPI lint** — runs the official [aep-openapi-linter](https://github.com/aep-dev/aep-openapi-linter)
   (a Spectral ruleset) against the generated `/openapi.json`.
2. **aepcli end-to-end** — drives the running server with [aepcli](https://github.com/aep-dev/aepcli)
   through the full CRUD lifecycle and **asserts on outcomes** (not manual inspection).

Both boot the host on an isolated in-memory store, so they leave no files and need no DB.

## Run

```bash
# everything (one server boot, both checks)
tests/conformance/run.sh

# just the OpenAPI lint
tests/conformance/lint-openapi.sh

# just the aepcli flow
tests/conformance/aepcli-conformance.sh
```

First run does `npm ci` to install the pinned linter (see `package.json` / `package-lock.json`).

### Requirements

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.x | builds/runs the host |
| Node.js | 20+ | runs Spectral |
| [aepcli](https://github.com/aep-dev/aepcli) | **≥ v0.3.0** (aep-lib-go Feb 2026+) | older builds drop `get`/`update`/`delete` for item paths |

Install a recent aepcli and (if needed) point the check at it:

```bash
go install github.com/aep-dev/aepcli/cmd/aepcli@latest
AEPCLI=$(go env GOPATH)/bin/aepcli tests/conformance/aepcli-conformance.sh
```

The aepcli check **probes the binary** (`publisher --help` must expose `get`/`delete`) and
fails fast with upgrade instructions if it's too old — encoding the README's version caveat
instead of leaving it to prose.

### Configuration (env vars)

| Var | Default | Meaning |
|-----|---------|---------|
| `AEPCLI` | `aepcli` | aepcli binary to use |
| `AEP_CONFORMANCE_PORT` | `5288` | port the test server listens on |
| `SPEC_FILE` | `/tmp/aep-conformance-openapi.json` | where the dumped spec is written |
| `REUSE_SPEC` | _(unset)_ | lint an existing `SPEC_FILE` without booting the server |

## Triaged deviations

The lint runs at `--fail-severity=warn`, so **any** error or warning fails the build. There are
currently **no rule overrides** in [`.spectral.yaml`](.spectral.yaml) — the generated spec passes
the full AEP ruleset clean. (The earlier `aep-134-content-type` and `aep-134-request-body`
deviations were resolved in [#11](../../docs/issues/11-merge-patch-content-type.md) and
[#10](../../docs/issues/10-patch-update-body-modeling.md) respectively.)

One intentional, ruleset-permitted quirk remains documented in
[docs/KNOWN_ISSUES.md](../../docs/KNOWN_ISSUES.md): the PATCH body references the resource schema
(matching aep-lib-go), so it advertises `required` fields even though updates are partial
server-side.

Everything else is expected to pass clean. When you change the OpenAPI generator
(`src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs`), re-run `lint-openapi.sh` — a new violation
fails the check immediately.

## How it maps to issue #07

This directory **is** [issue #07](../../docs/issues/07-aeplinter-aepcli-conformance.md):
pinned linter versions, a scripted aepcli flow with assertions, an encoded aepcli version gate,
and a one-command runner. CI wiring lives in `.github/workflows/conformance.yml`.
