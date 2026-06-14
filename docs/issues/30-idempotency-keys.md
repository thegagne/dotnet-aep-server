# 30 — Idempotency keys (AEP-155)

**Theme:** API surface (AEP) / Reliability · **Status:** proposed

## Summary

Let clients safely retry writes by sending an idempotency key — a repeated `Create` (or other
mutation) with the same key returns the original result instead of creating a duplicate, per
AEP-155.

## Motivation

Network retries on `POST` create duplicates today (user-settable id mitigates `Create`, but not
generally). An idempotency key makes retries safe — important for at-least-once callers, gateways,
and the resilience retries in [#23](23-storage-resilience.md).

## Scope

- Accept an `Idempotency-Key` header (and/or AEP `request_id`) on mutating methods.
- Persist `(key → result/response, status)` for a TTL; a repeat within the window short-circuits to
  the stored response; a different payload under the same key → `409` (conflict).
- Storage for the key↔result map (a TTL'd table/attribute per backend; DynamoDB TTL is natural).
- Scope keys per client ([#16](16-oauth-client-credentials-validator.md)) to avoid cross-tenant collisions.

## Acceptance criteria

- [ ] A repeated mutation with the same key returns the original result, no duplicate side effect.
- [ ] Same key + different body → `409`; keys expire after a configurable TTL.
- [ ] Works across backends; keys are client-scoped when auth is on.

## References

- AEP-155 (idempotency / request identification); aep-components `idempotency_key`; relates to
  [#23](23-storage-resilience.md), [#27](27-batch-methods.md), [#16](16-oauth-client-credentials-validator.md)
