# 12 — `etag` and preconditions (AEP-154)

**Theme:** API surface / standard fields · **Status:** ✅ done (store-level atomic)

## Delivered

- **ETag** computed by `src/Aep.AspNetCore/Http/ETag.cs` (SHA-256 over path + update timestamp;
  quoted, opaque, changes on every write). Emitted as an `ETag` header on all single-resource
  responses (Get/Create/Update/Apply).
- **If-Match** honored on `GET`/`PATCH`/`PUT`/`DELETE`: mismatch or absent resource → **412**.
  `*` matches any current version.
- **Store-level atomicity (chosen approach):** `IResourceStore.UpdateAsync`/`DeleteAsync` take an
  optional `expectedUpdateTime`; each backend does a conditional write —
  `… WHERE update_time = ?` (SQLite/Postgres), `ConditionExpression #ut = :expected` (DynamoDB),
  compare-under-lock (in-memory). The controller pre-reads to compare the ETag and then guards the
  write on the same version, so a concurrent writer in the read→write window still loses → 412.
- Update timestamps bumped to microsecond precision so consecutive writes get distinct version
  tokens (`DefaultResourceBackend.Rfc3339Now`).
- **If-None-Match** is explicitly unsupported → **400** (per AEP-154's "unsupported conditional
  header must not be ignored"). See open item below.
- **OpenAPI**: `If-Match` header param + `412` response on item ops, `ETag` response header on
  single-resource 200s.
- Tests: `tests/Aep.Server.Tests/EtagConcurrencyTests.cs` (8 cases — emit/stability, match/stale,
  wildcard, missing→412, delete-if-match, get-if-match, if-none-match→400). All four store suites
  still pass; conformance green.

## Open / deferred

- `If-None-Match` (304 caching on GET; `*` create-only) is rejected rather than supported. AEP
  lists it as **may**; supporting it is a clean follow-up.
- The pre-read + conditional-write is atomic for the write itself; the ETag *comparison* uses the
  pre-read value, and the conditional write closes the race. A single-round-trip variant (encode
  the version in the ETag) could drop the pre-read but trades off ETag opacity.

## Summary

Add the standard `etag` concurrency token and RFC 9110 preconditions (`ETag` / `If-Match` /
`If-None-Match` headers) so clients can do safe optimistic concurrency, per AEP-154.

## Motivation

AEP-154 (Preconditions) lets a client and server agree on a resource's current state before a
mutation, preventing lost updates from concurrent writers. It's a core "standard field"
expected of an AEP-compliant API, split out of [#02](02-aep-compliant-field-types.md) because
it's a behavior feature (not just a type) and has real correctness depth.

## Current state

- No `etag` is computed or returned; no conditional-header handling
  (`src/Aep.AspNetCore/Controllers/ResourceController.cs`, `Http/ResourceResponse.cs`).
- Mutations are unconditional read-modify-write through `IResourceBackend` / `IResourceStore`.

## What AEP-154 / RFC 9110 require

- **ETag** computed as an opaque hash that changes whenever the resource changes; emitted on
  single-resource responses; **quoted** (`ETag: "…"`).
- **If-Match** on mutations (`PATCH`/`PUT`/`DELETE`): mismatch → **412 Precondition Failed**.
- **If-None-Match** may be supported (`*` = "must not exist" for create-style writes; GET → 304).
- **All-or-nothing**: if any conditional header is honored for any mutation, all mutation
  methods must honor `If-Match`/`If-None-Match` consistently, and an *unsupported* conditional
  header must yield `400 INVALID_ARGUMENT` (never be silently ignored).

## Proposed scope

1. Compute `etag` deterministically (e.g. `sha256` of path + update_time + canonical field JSON);
   expose it both as an `ETag` response header and (optionally) an `etag` body field; reserve it
   as a server-managed standard field in `SchemaValidator`.
2. Honor `If-Match` on `PATCH`/`PUT`/`DELETE` (and `If-Match`/`If-None-Match` on `GET`), returning
   412 on mismatch; return 400 for unsupported conditional headers.
3. **Atomicity** — the key design decision:
   - *Option A (simple):* compare in the controller via a pre-read. Correct for the common
     case but has a TOCTOU window between read and write.
   - *Option B (robust):* push an `expectedVersion`/`expectedUpdateTime` into
     `IResourceStore.UpdateAsync`/`DeleteAsync` so each backend does a conditional write
     (`… WHERE path=? AND update_time=?`). Atomic, but touches all four stores.
   Recommend B for real concurrency safety; A is acceptable as a documented first step.
4. Reflect `etag` + the conditional behavior in the OpenAPI spec.

## Acceptance criteria

- [x] Single-resource responses include a quoted `ETag` header that changes on modification.
- [x] `If-Match` mismatch on a mutation returns 412; matching proceeds.
- [x] Conditional-header support is uniform across mutation methods; unsupported (If-None-Match) → 400.
- [x] Concurrency atomicity: **store-level** (Option B) — conditional write enforced in all four stores.
- [x] Tested across backends and surfaced in OpenAPI.

## References

- AEP-154 (preconditions); RFC 9110 §13 (conditional requests)
- `src/Aep.AspNetCore/Controllers/ResourceController.cs`, `Http/ResourceResponse.cs`
- `src/Aep.Storage.Abstractions/Storage/IResourceStore.cs` (for Option B)
- Split from [#02](02-aep-compliant-field-types.md)
