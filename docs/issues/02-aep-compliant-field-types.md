# 02 — AEP-compliant configurable field types

**Theme:** Modeling · **Status:** ✅ done — behaviors, value validation, and standard fields (`uid`, `etag`) delivered across increments. Nested objects are out of scope; the remaining PATCH-`$ref` modeling lives in [#10](10-patch-update-body-modeling.md).

## Delivered — increment 1: field behaviors (AEP-203)

- `immutable` and `input_only` added to the model (`SchemaProperty`), parsed from YAML by
  `ServiceDefinitionLoader`, alongside the existing `read_only`, `enum`, and `format`.
- **Enforcement** (`SchemaValidator`): `immutable` fields are rejected on PATCH
  (`400 INVALID_ARGUMENT`); `read_only` rejected on all writes; `required` on Create/Apply.
- **Responses** (`ResourceResponse.ToBody`): `input_only` fields are never returned.
- **OpenAPI** (`OpenApiGenerator`): native `readOnly`/`writeOnly` plus an `x-aep-field-behavior`
  array (`OUTPUT_ONLY`/`IMMUTABLE`/`INPUT_ONLY`); standard `id`/`path`/`create_time`/`update_time`
  marked `OUTPUT_ONLY`.
- Tests: `tests/Aep.Server.Tests/FieldBehaviorTests.cs` (loader, validator, response, OpenAPI).
- Docs: README → "Field types and behaviors". Conformance suite stays green.

## Delivered — increment 2: stronger value validation

- Numeric **format** validation in `SchemaValidator`: `format: int32` enforces the 32-bit range
  (`int64`/unspecified → 64-bit); error messages name the format.
- **Recursive array-item validation**: each element is checked against the `items` schema
  (type and enum) — previously array contents were unchecked. Errors are indexed (`tags[1]`).
- Tests added to `FieldBehaviorTests.cs`; README "Field types and behaviors" updated.

**Still open**: deep nested object/map modeling is **out of scope** (per maintainer). Remaining:
the per-verb `required` modeling that [#10](10-patch-update-body-modeling.md) needs, and the
two standard-field features split into their own issues —
[#12 `etag`/preconditions](12-etag-preconditions.md) and [#13 system-assigned `uid`](13-system-assigned-uid.md).

## Summary

Expand the schema vocabulary in `resources.yaml` so resource fields can use the full set
of AEP-standard types and behaviors — not just the JSON-schema primitives (`string`,
`integer`, `boolean`, `array`) currently supported — and have them validated, stored, and
reflected in OpenAPI consistently.

## Motivation

AEP defines conventions for common field shapes that clients and tools (aepcli, ui.aep.dev,
linters) rely on. The sample today uses only bare primitives. To call ourselves
"AEP-compliant" for modeling, the server should support:

- **Standard fields** (AEP-148): `create_time`, `update_time`, `uid`, `etag`, `display_name`,
  with reserved semantics (server-set, output-only).
- **Timestamps** (AEP-142): RFC 3339 strings, output-only `create_time` / `update_time`.
- **Enumerations** (AEP-126): a constrained set of string values, surfaced as an OpenAPI enum.
- **Numbers / formats**: `int32` vs `int64`, `double`, and `format` hints.
- **Field behaviors**: `required`, `output_only`, `input_only`, `immutable` — affecting
  validation on Create vs. Update/Apply.
- **Nested objects / maps** beyond flat scalar + `array<string>`.

## Current state

- `resources.yaml` schemas use a JSON-schema-ish subset; see the sample
  (`src/Aep.Server/resources.yaml`) — `string`, `integer`, `boolean`, `array<string>`.
- Validation lives in `src/Aep.AspNetCore/Http/SchemaValidator.cs`.
- `create_time` / `update_time` are tracked by the store (see `UpdateAsync(... updateTime ...)`
  in `IResourceStore`) but field-type semantics aren't modeled declaratively.

## Proposed scope

1. Define the supported type/behavior vocabulary and document it (a "field types" section).
2. Extend the schema loader (`src/Aep.AspNetCore/Configuration/ServiceDefinitionLoader.cs`)
   and model (`src/Aep.Storage.Abstractions/Model/`) to carry type + behavior metadata.
3. Enforce field behaviors in `SchemaValidator`:
   - `output_only` rejected on write, populated by server;
   - `immutable` rejected on Update/Apply once set;
   - `required` enforced on Create.
4. Map standard fields (AEP-148) to reserved, server-managed semantics.
5. Surface enums, formats, and behaviors in the OpenAPI 3.1 output (`src/Aep.AspNetCore/OpenApi/`).
6. Ensure each storage backend round-trips the richer types (timestamps, enums, nested).

## Acceptance criteria

- [x] `create_time` / `update_time` are modeled as output-only timestamps and reflected in OpenAPI.
- [x] An enum field rejects out-of-set values with `400 INVALID_ARGUMENT` and appears as an OpenAPI enum.
- [x] `output_only` / `immutable` / `input_only` behaviors are enforced on the correct verbs.
- [x] Field behaviors are documented (README) and exercised by tests. _(Behaviors live in the
      validator/response layer, so they're backend-agnostic; value round-tripping is already
      covered by the per-store tests.)_
- [x] `int32`/`int64` range validation and recursive array-item validation.
- [x] Standard fields beyond the four: `uid` ✅ [#13](13-system-assigned-uid.md); `etag` ✅
      [#12](12-etag-preconditions.md). (`display_name` is a plain string field; no behavior needed.)
- [x] ~~Deep nested object/map types~~ — out of scope per maintainer.

## Related conformance deviations

The conformance check ([#07](07-aeplinter-aepcli-conformance.md)) suppresses two AEP-134
linter rules, each tracked by its own issue. The first depends on this one:

- [**#10 — Update body modeling**](10-patch-update-body-modeling.md) (`aep-134-request-body`):
  PATCH can reference the resource cleanly once per-verb field behaviors here make
  "required on create only" expressible.
- [**#11 — merge-patch content type**](11-merge-patch-content-type.md) (`aep-134-content-type`):
  independent of this issue; listed for completeness.

## References

- AEP-126 (enumerations), AEP-134 (Update / merge-patch), AEP-142 (timestamps), AEP-148 (standard fields)
- `src/Aep.AspNetCore/Http/SchemaValidator.cs`, `src/Aep.AspNetCore/OpenApi/`
- `src/Aep.Storage.Abstractions/Model/`
- Related: [07 — aeplinter/aepcli conformance](07-aeplinter-aepcli-conformance.md)
