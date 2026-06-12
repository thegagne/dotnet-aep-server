# 10 — Model the Update (PATCH) body as a resource reference

**Theme:** API surface / conformance · **Status:** ✅ done (Option A)

## Resolution

Implemented **Option A**, matching the canonical generator. Confirmed by reading
`aep-lib-go/pkg/api/openapi.go`: it models the Update body as a `$ref` to the resource schema
under `application/merge-patch+json` — no derived patch schema, `required` not stripped. Our
`OpenApiGenerator` now does the same (`PartialUpdateSchema` removed); the `aep-134-request-body`
suppression is gone, so the conformance check passes the **full** AEP ruleset with **zero**
overrides.

The only trade-off — PATCH advertising `required` fields even though updates are partial
server-side — is exactly how the ecosystem models it, and is documented in
[docs/KNOWN_ISSUES.md](../KNOWN_ISSUES.md). Option B (a derived patch component) was rejected: it
would diverge from aep-lib-go and risk duplicate `x-aep-resource` annotations confusing resource
discovery.

## Summary

Make the PATCH (Update) request body reference the resource so it passes the
`aep-134-request-body` linter rule, while preserving correct partial-update (merge) semantics —
then re-enable the rule in `tests/conformance/.spectral.yaml`.

## Motivation — the finding

The conformance lint ([#07](07-aeplinter-aepcli-conformance.md)) reports an **error**:

```
aep-134-request-body  The request body is not an AEP resource.
  paths./publishers/{publisher_id}.patch.requestBody.content.application/json.schema
```

AEP-134 expects the Update body to be the resource. The rule is currently **suppressed** in
`tests/conformance/.spectral.yaml`; this issue removes that suppression for real.

## Current state — and a corrected assumption

- PATCH emits an **inline partial schema** (writable properties, no `required`) via
  `PartialUpdateSchema(...)` in `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs`, instead of a
  `$ref` to the resource component. That inline schema is why the rule fails (it's not a
  recognizable AEP resource).
- The original rationale for the inline schema was "a `$ref` would make aepcli demand
  `required` fields (e.g. `--title`) on update." **Verified false for aepcli v0.3.0**: aepcli
  derives `required` flags from the resource component's `required`, so `book update` demands
  `--title` *even with the current inline-partial body*. The inline schema does not change
  aepcli's behavior — so switching to a resource reference is **not** an aepcli regression.
- The real concern is OpenAPI *semantics*: a naive `$ref` to the full resource schema would
  advertise that PATCH requires `title`, which is wrong for a partial update.

## Proposed scope

1. Reference an AEP resource schema from the PATCH body (satisfying `aep-134-request-body`)
   without advertising create-only `required` fields on a partial update. Options to evaluate:
   - a dedicated patch/update component schema derived from the resource (resource minus
     `required`, output-only fields stripped), still carrying enough to be recognized; or
   - `$ref` the resource component once per-verb field behaviors from [#02](02-aep-compliant-field-types.md)
     make "required on create only" expressible, so the resource schema's `required` no longer
     implies required-on-PATCH.
2. Keep server-side merge semantics unchanged (`SchemaValidator.ValidateForPatch`).
3. Confirm aepcli still drives `update` correctly (it will still ask for `--title`; that's an
   aepcli behavior, documented in the README, not introduced here).
4. Remove the `aep-134-request-body: off` override and confirm the lint passes.

## Acceptance criteria

- [x] PATCH request body references an AEP resource schema; `aep-134-request-body` passes.
- [~] ~~The advertised PATCH body does not mark create-only fields as required.~~ Superseded:
      matching aep-lib-go means the body *does* reference the resource (with its `required`); the
      partial semantics live server-side and the quirk is documented in
      [docs/KNOWN_ISSUES.md](../KNOWN_ISSUES.md).
- [x] `tests/conformance/run.sh` passes with the override removed (zero suppressions).
- [x] aepcli update flow in `aepcli-conformance.sh` still passes.

## References

- AEP-134 (Update); `aep-openapi-linter` rule `aep-134-request-body`
- `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs` (`PartialUpdateSchema`, `WriteItemOperation`)
- `tests/conformance/.spectral.yaml`
- Depends on / relates to: [02 — AEP-compliant field types](02-aep-compliant-field-types.md)
  (per-verb field behaviors); sibling: [11 — merge-patch content type](11-merge-patch-content-type.md)
