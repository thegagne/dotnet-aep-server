# 18 — Rate limiting / throttling

**Theme:** Security / Reliability · **Status:** proposed

## Summary

Protect the server from abuse and noisy neighbors with configurable request rate limiting —
per-client (when authenticated) and/or per-IP — returning `429 Too Many Requests` with
`Retry-After`.

## Scope

- Use ASP.NET Core's built-in rate limiter middleware (token-bucket / fixed-window / concurrency
  limiter), configured via `appsettings`/env.
- **Partition by `client_id`** when OAuth ([#16](16-oauth-client-credentials-validator.md)) is on,
  else by IP (honoring forwarded headers behind a gateway).
- Optional per-method or read/write tiers (writes cheaper budget than reads, or vice versa).
- Emit `429` in the AEP-193 error shape with `Retry-After`; surface limiter rejections in
  telemetry ([#17](17-observability-opentelemetry.md)).
- Off by default; opt-in.

## Acceptance criteria

- [ ] Configurable limits enforced per client/IP; over-limit → `429` + `Retry-After`.
- [ ] Partitions by `client_id` when auth is enabled, IP otherwise (forwarded-header aware).
- [ ] Rejections are observable (counter + log) and don't leak limiter internals.

## References

- ASP.NET Core rate limiting middleware; relates to [#16](16-oauth-client-credentials-validator.md),
  [#17](17-observability-opentelemetry.md), [#14](14-resilient-extension-points.md)
