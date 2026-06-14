# 27 — Batch methods (AEP-231/233/234/235)

**Theme:** API surface (AEP) · **Status:** proposed

## Summary

Add the AEP batch standard methods — `BatchGet`, `BatchCreate`, `BatchUpdate`, `BatchDelete` — so
clients can operate on many resources of a collection in one round trip.

## Scope

- Routes per AEP: `GET .../books:batchGet`, `POST .../books:batchCreate`, `:batchUpdate`,
  `:batchDelete` (collection-scoped; child of the same parent).
- Request/response shapes per AEP (lists of resources / ids; responses preserve order).
- **Atomicity** — the key decision: all-or-nothing vs. partial success. AEP batch create/update/
  delete are **atomic** (all succeed or none); model it with a store-level transaction where the
  backend supports it (Postgres/SQLite) and document the DynamoDB approach (`TransactWriteItems`,
  with its 100-item / size limits).
- Bounded batch size; surface partial-failure rules clearly.
- Reflect in OpenAPI; verify aepcli/linter happy.

## Acceptance criteria

- [ ] The four batch methods work collection-scoped with AEP-shaped request/response.
- [ ] Atomicity is enforced (or its limits documented per backend); batch size is bounded.
- [ ] Surfaced in OpenAPI and conformance-checked.

## References

- AEP-231 (Batch Get), 233 (Batch Create), 234 (Batch Update), 235 (Batch Delete);
  DynamoDB `TransactWriteItems`/`BatchGetItem`; relates to [#12](12-etag-preconditions.md) (per-item preconditions)
