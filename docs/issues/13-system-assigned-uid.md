# 13 — System-assigned `uid` standard field (AEP-148)

**Theme:** Storage / standard fields · **Status:** ✅ done

## Delivered

- `Uid` added to `StoredResource` (output-only, immutable); generated as a UUID on create in
  `DefaultResourceBackend` (both Create and the create branch of Apply).
- Persisted and round-tripped by **all four** backends — a nullable `uid` column for
  SQLite/Postgres, a `uid` attribute for DynamoDB, and the in-memory copy paths. Existing
  databases are migrated in `EnsureSchemaAsync` (SQLite `pragma_table_info` guard; Postgres
  `ADD COLUMN IF NOT EXISTS`); legacy rows read back `uid: null` and are simply omitted.
- Reserved in every standard-field set (validator, response builder, each store, index
  validation), so clients can't set it; returned by `ResourceResponse.ToBody`.
- Surfaced in OpenAPI as an `OUTPUT_ONLY` string (reusing #02's `x-aep-field-behavior`).
- Tests: `ResourceApiTests.Uid_is_assigned_stable_and_not_client_settable` (assignment,
  client value ignored, stable across update, fresh uid after delete+recreate) plus an OpenAPI
  assertion; Postgres/DynamoDB store tests exercise round-trip on fresh schemas.

**Note:** the `id`/`uid` distinction means a client-supplied `uid` on write is ignored, not
rejected (consistent with how other output-only standard fields are handled).

## Summary

Add the AEP-148 `uid` standard field: a server-assigned, globally-unique, **immutable**,
output-only identifier that stays stable for the lifetime of a resource — even if a resource
with the same id is deleted and recreated.

## Motivation

The resource `id` (and full `path`) can be reused after deletion; `uid` gives clients a stable
handle that never collides or repeats. It's a standard field (AEP-148) and complements the
field-behavior work already done in [#02](02-aep-compliant-field-types.md) (it's exactly an
`OUTPUT_ONLY` + `IMMUTABLE` field) — split out because, unlike behaviors, it requires storage.

## Current state

- `StoredResource` models `Id`, `Path`, `CreateTime`, `UpdateTime` — no `uid`
  (`src/Aep.Storage.Abstractions/Model/StoredResource.cs`).
- Standard fields are reserved in `SchemaValidator` and emitted by `ResourceResponse.ToBody` /
  `OpenApiGenerator` (`id`, `path`, `create_time`, `update_time`).

## Proposed scope

1. Add `Uid` to `StoredResource`; generate a UUID at create time.
2. Persist it in every backend — a column for SQLite/Postgres, an attribute for DynamoDB,
   a field for in-memory (`src/Aep.Storage.*`), including `EnsureSchemaAsync` migrations.
3. Reserve `uid` in `SchemaValidator` (clients may not set it) and return it from
   `ResourceResponse.ToBody`.
4. Surface `uid` in the OpenAPI resource schema as `OUTPUT_ONLY` (reuse the
   `x-aep-field-behavior` support added in #02).
5. Tests: generation, immutability/stability across update, uniqueness across delete+recreate,
   and round-trip per backend.

## Acceptance criteria

- [x] Every created resource gets a unique `uid`, returned in responses and OpenAPI.
- [x] `uid` is stable across updates and not settable by clients.
- [x] Deleting and recreating the same `id` yields a different `uid`.
- [x] Persisted and round-tripped by all four backends (covered by the per-store tests).

## References

- AEP-148 (standard fields)
- `src/Aep.Storage.Abstractions/Model/StoredResource.cs`, `src/Aep.Storage.*`
- `src/Aep.AspNetCore/Http/SchemaValidator.cs`, `Http/ResourceResponse.cs`, `OpenApi/OpenApiGenerator.cs`
- Split from [#02](02-aep-compliant-field-types.md)
