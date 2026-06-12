# 07 — Conformance testing with aeplinter + aepcli

**Theme:** Quality · **Status:** ✅ done — see [`tests/conformance/`](../../tests/conformance/)

## Delivered

- [`tests/conformance/`](../../tests/conformance/): `run.sh` (both checks, one server boot),
  `lint-openapi.sh`, `aepcli-conformance.sh`, shared `_lib.sh`.
- **OpenAPI lint** via Spectral + `@aep_dev/aep-openapi-linter`, **pinned** in `package.json` /
  `package-lock.json`; gate runs at `--fail-severity=warn`.
- Fixed the real findings the linter surfaced: the AEP-193 `Error` schema's `type` and
  `instance` now carry `format: uri-reference` (`src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs`).
- Two deviations triaged off with rationale in `.spectral.yaml`, each with its own follow-up:
  [#10 (PATCH body modeling)](10-patch-update-body-modeling.md) and
  [#11 (merge-patch content type)](11-merge-patch-content-type.md).
- **aepcli flow** asserts full CRUD for a top-level + nested resource; **probes** the binary and
  fails fast (with install instructions) if it predates the `get`/`update`/`delete` fix
  (needs aepcli ≥ v0.3.0 / aep-lib-go Feb 2026+).
- CI: [`.github/workflows/conformance.yml`](../../.github/workflows/conformance.yml).
- README updated; manual aepcli notes retained under "Driving it with aepcli by hand".

## Summary

Make external AEP conformance a first-class, ideally automated check: lint the generated
OpenAPI spec with **aeplinter**, and exercise the running server with **aepcli** as an
end-to-end conformance pass — beyond the manual instructions in the README.

## Motivation

The server's claim to AEP compliance is only as good as what an AEP-aware tool says about it.
The README already shows aepcli usage manually and notes spec/parser caveats. Turning these
into repeatable checks catches drift the moment the OpenAPI output stops being conformant —
especially as field types ([02](02-aep-compliant-field-types.md)), wildcard list
([04](04-reading-across-collections.md)), and LRO ([05](05-long-running-operations.md)) change the spec.

## Current state

- README "Testing with aepcli" documents a manual flow against `/openapi.json`, plus known
  caveats (older aepcli parser bug; required-field handling on update; 204 cosmetic error).
- No aeplinter step anywhere.
- No CI job that boots the server and runs an external tool against it.

## Proposed scope

1. **aeplinter** — add a step that runs the linter against the generated `/openapi.json`
   (or a dumped spec file) and fails on violations. Capture/triage any rules we intentionally
   don't follow, with rationale.
2. **aepcli conformance script** — script the create/get/list/filter/update/delete flow
   (extending the README example) as a runnable check; assert outcomes rather than eyeballing.
3. **Pin tool versions** — record the aepcli / aep-lib-go version that's known-good (README
   notes Feb 2026+), so the check is reproducible and the known caveats are encoded.
4. **CI integration** — run both in CI where feasible (these need the Go tools + a running
   server); otherwise provide a single local script and document it.
5. Fold results back into the README, replacing "here's how to run it manually" with "here's
   the conformance check."

## Acceptance criteria

- [x] aeplinter runs against the generated OpenAPI spec; violations fail the check.
- [x] A scripted aepcli flow asserts CRUD outcomes (not manual inspection).
- [x] Known caveats are encoded (pinned linter version; aepcli version probe) rather than just described in prose.
- [x] Conformance check is runnable with one command and documented.

> Filter assertions via aepcli were left out — aepcli doesn't expose a `--filter` flag, and CEL
> filtering is already covered by unit tests. Revisit if aepcli gains filter support.

## References

- README → "Testing with aepcli"; [aepcli](https://github.com/aep-dev/aepcli)
- `src/Aep.AspNetCore/OpenApi/` (spec generation)
- Related: [02 — field types](02-aep-compliant-field-types.md), [06 — expanded tests](06-expanded-test-suite-and-performance.md)
