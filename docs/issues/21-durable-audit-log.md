# 21 — Durable audit log

**Theme:** Security · **Status:** proposed

## Summary

A durable, queryable audit trail of write operations — *who* changed *what*, *when*, and the
*outcome* — beyond the in-process canonical log line. Split from [#16](16-oauth-client-credentials-validator.md)
/ [#17](17-observability-opentelemetry.md), where the basic record lives but a tamper-resistant,
retained store does not.

## Scope

- Record per mutating operation: timestamp, `client_id` (from [#16](16-oauth-client-credentials-validator.md)),
  AEP method, resource path, before/after summary (or a content hash — **no raw field values / PII**
  by default), and outcome.
- **Pluggable sink** — structured log (v1) → a durable, append-only store (DynamoDB/Postgres table,
  or an external audit service) with retention.
- Tamper-evidence (hash chaining / append-only) for the durable sink.
- Include reads only if explicitly enabled (volume/sensitivity).
- Implemented as a backend decorator so it captures every operation uniformly.

## Acceptance criteria

- [ ] Every write is audited with caller + method + path + outcome; tokens/values are not stored.
- [ ] The sink is pluggable; a durable, append-only option exists with retention.
- [ ] Audit failures don't silently drop records (fail-closed or buffered, configurable).

## References

- Splits from [#16](16-oauth-client-credentials-validator.md), [#17](17-observability-opentelemetry.md);
  uses the `ResourceBackendDecorator` seam
