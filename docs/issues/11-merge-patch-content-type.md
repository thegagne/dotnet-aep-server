# 11 — Advertise (and validate) `application/merge-patch+json` for PATCH

**Theme:** API surface / conformance · **Status:** ✅ done

## Delivered

- PATCH now advertises `application/merge-patch+json` (and only PATCH); Create/Apply stay on
  `application/json`. `JsonRequestBody`/`WriteItemOperation` in
  `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs` take a content type, defaulting to
  `application/json`.
- **Inbound handling: lenient (chosen).** The server keeps reading the raw body regardless of
  `Content-Type` (`ReadBodyAsync`), so existing clients sending `application/json` to PATCH keep
  working — advertising the correct type is purely additive, no `415`. Revisit if strict
  negotiation is ever wanted.
- `aep-134-content-type` re-enabled (suppression removed from `tests/conformance/.spectral.yaml`);
  `tests/conformance/run.sh` is green and the aepcli update flow still passes.
- Updated the OpenAPI assertion in `tests/Aep.Server.Tests/ResourceApiTests.cs`.

## Summary

Advertise `application/merge-patch+json` as the PATCH (Update) request content type per
AEP-134, and re-enable the `aep-134-content-type` linter rule.

## Motivation — the finding

The conformance lint ([#07](07-aeplinter-aepcli-conformance.md)) reports a **warning**:

```
aep-134-content-type  The request body content type should be "application/merge-patch+json"
  paths./publishers/{publisher_id}.patch.requestBody.content
```

AEP-134 models Update as a JSON Merge Patch (RFC 7386), whose media type is
`application/merge-patch+json`. The server's Update *is* a merge (README: "Update (merge)"),
so advertising `application/json` is simply the wrong content type. The rule is currently
**suppressed** in `tests/conformance/.spectral.yaml`; this issue removes that suppression.

## Current state — and a corrected assumption

- The OpenAPI generator emits PATCH bodies under `application/json` via `JsonRequestBody(...)`
  in `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs`.
- The original suppression rationale claimed the server "only binds `application/json`."
  **Verified false**: the controller reads the raw request body in `ReadBodyAsync()`
  (`src/Aep.AspNetCore/Controllers/ResourceController.cs`) and is content-type-agnostic — a
  `PATCH` with `Content-Type: application/merge-patch+json` already returns `200` and applies
  the merge. So this is mostly a *spec advertising* fix, not a server capability gap.

## Proposed scope

1. Emit the PATCH request body under `application/merge-patch+json` (a PATCH-specific variant
   of `JsonRequestBody`), leaving Create/Apply on `application/json`.
2. Decide on inbound validation: either keep accepting any JSON content type (lenient, current
   behavior) or validate the PATCH `Content-Type` and reject mismatches with `415` — document
   the choice. Lenient acceptance plus correct advertising is the low-risk default.
3. Confirm aepcli reads the new content type and PATCH still works end-to-end.
4. Remove the `aep-134-content-type: off` override and confirm the lint passes.

## Acceptance criteria

- [x] OpenAPI advertises `application/merge-patch+json` for PATCH (and only PATCH).
- [x] `aep-134-content-type` passes with the override removed; `tests/conformance/run.sh` is green.
- [x] aepcli update flow in `aepcli-conformance.sh` still passes.
- [x] Inbound content-type handling (lenient vs. strict 415) is documented (lenient — see above).

## References

- AEP-134 (Update); RFC 7386 (JSON Merge Patch); rule `aep-134-content-type`
- `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs` (`JsonRequestBody`, `WriteItemOperation`)
- `src/Aep.AspNetCore/Controllers/ResourceController.cs` (`ReadBodyAsync`)
- `tests/conformance/.spectral.yaml`
- Sibling: [10 — Update body modeling](10-patch-update-body-modeling.md)
