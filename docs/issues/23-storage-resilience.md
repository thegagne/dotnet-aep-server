# 23 — Storage resilience: retries, backoff, circuit breaker, timeouts

**Theme:** Reliability · **Status:** proposed

## Summary

Make storage access resilient to transient failures — retry idempotent operations with backoff,
trip a circuit breaker when a backend is unhealthy, and bound every storage call with a timeout —
so a flaky DB degrades gracefully instead of hanging or cascading.

## Scope

- A resilience **decorator over `IResourceStore`** (clean seam, backend-agnostic) using a policy
  library (e.g. `Microsoft.Extensions.Http.Resilience` / Polly).
- **Retry with exponential backoff + jitter** for transient errors only (timeouts, connection
  resets, DynamoDB throttling / Postgres serialization failures) — **only on safe operations**
  (reads; writes that are idempotent or guarded by the etag precondition from [#12](12-etag-preconditions.md)).
- **Circuit breaker** per backend — fail fast (→ `503`) when error rate is high, with half-open recovery.
- **Per-call timeout** (distinct from the command timeout in [#01](01-configurable-database-connections.md))
  so a stuck call is abandoned.
- All tunable via config; resilience events surface in telemetry ([#17](17-observability-opentelemetry.md)).

## Acceptance criteria

- [ ] Transient storage errors are retried with backoff+jitter; non-transient and unsafe ops are not.
- [ ] A circuit breaker trips on sustained failures and recovers; tripped → `503`.
- [ ] Every storage call is timeout-bounded; retries/timeouts/breaker state are observable.

## References

- Polly / `Microsoft.Extensions.Resilience`; relates to [#01](01-configurable-database-connections.md),
  [#12](12-etag-preconditions.md), [#17](17-observability-opentelemetry.md); seam: `IResourceStore`
