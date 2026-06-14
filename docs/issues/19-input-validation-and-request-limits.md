# 19 — Stronger input validation + request limits

**Theme:** Security / Modeling · **Status:** proposed

## Summary

Close the input-validation gaps (today `SchemaValidator` does only type / required / enum / int32 /
array-item checks) by adding **constraint validation** and **request limits**, so malformed or
abusive payloads are rejected with `400` before they reach storage.

## Motivation

The server validates *shape* but not *bounds*. There's no `maxLength`/`pattern`, no numeric
`min`/`max`, no string `format` validation, no resource-id format check, and no app-level body
size/depth guard — each a correctness or DoS gap.

## Scope

**Schema constraints** (extend the `SchemaProperty` model + `SchemaValidator`, declared in `resources.yaml`):
- strings: `maxLength` / `minLength` / `pattern` (regex) / `format` (email, uri, date-time, uuid)
- numbers: `minimum` / `maximum`
- arrays: `minItems` / `maxItems` / `uniqueItems`
- optional **strict mode**: reject unknown fields instead of silently dropping them

**Resource-id validation** — enforce AEP-122 id rules on `?id=` (charset, length, no `/`).

**Request limits** (pipeline-level DoS guard):
- max request body size, max JSON nesting depth, max array element count
- reject oversized/over-deep payloads with `400`/`413` before full parse

## Acceptance criteria

- [ ] String/number/array constraints from `resources.yaml` are enforced; violations → `400` (AEP-193).
- [ ] Resource ids are format-validated; bad ids → `400`.
- [ ] Body size / nesting depth limits reject abusive payloads with a clear error.
- [ ] Optional strict-unknown-fields mode is available (default remains lenient).
- [ ] Constraints surface in OpenAPI (`maxLength`, `pattern`, `minimum`, …).

## References

- AEP-122 (resource ids); JSON Schema validation keywords; relates to
  [#02](02-aep-compliant-field-types.md), [#15](15-resource-examples-in-openapi.md)
- `src/Aep.AspNetCore/Http/SchemaValidator.cs`, `src/Aep.Storage.Abstractions/Model/ResourceSchema.cs`
