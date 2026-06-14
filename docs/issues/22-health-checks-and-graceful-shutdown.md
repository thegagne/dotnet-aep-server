# 22 — Health checks + graceful shutdown

**Theme:** Reliability / Ops · **Status:** proposed

## Summary

Add liveness/readiness health endpoints and graceful shutdown so the server plays well with load
balancers, Kubernetes, and rolling deploys.

## Scope

- **`/healthz` (liveness)** — process is up; cheap, no dependencies.
- **`/readyz` (readiness)** — dependencies reachable (storage `EnsureSchema`/ping ok); fails during
  startup and when the backend is unavailable, so traffic isn't routed prematurely.
- Health endpoints are unauthenticated and excluded from rate limiting / the canonical log noise.
- **Graceful shutdown** — on SIGTERM, stop accepting new requests and drain in-flight ones within a
  configurable timeout before exiting (host lifetime + connection draining).
- Per-backend readiness checks (SQLite file/connection, Postgres `SELECT 1`, DynamoDB `DescribeTable`/
  endpoint reachability).

## Acceptance criteria

- [ ] `/healthz` returns 200 when up; `/readyz` returns 503 until the backend is reachable.
- [ ] Readiness reflects real storage health per backend.
- [ ] In-flight requests drain on shutdown within the configured window.
- [ ] Health endpoints bypass auth and rate limiting.

## References

- ASP.NET Core HealthChecks + `IHostApplicationLifetime`; relates to
  [#16](16-oauth-client-credentials-validator.md), [#18](18-rate-limiting.md)
