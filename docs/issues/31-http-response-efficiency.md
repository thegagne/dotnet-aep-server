# 31 — HTTP response efficiency: compression + ETag conditional GET

**Theme:** Performance · **Status:** proposed

## Summary

Cut bytes on the wire: response **compression** (gzip/brotli) and **conditional GET** via
`If-None-Match` → `304 Not Modified`, completing the conditional-request story started in
[#12](12-etag-preconditions.md) (which shipped `If-Match`/`412` but defers `If-None-Match`).

## Scope

- **Compression** — enable response compression middleware for JSON, negotiated via `Accept-Encoding`
  (brotli/gzip), with a min-size threshold; safe (no compress-then-encrypt concern for a JSON API).
- **Conditional GET** — `GET` with `If-None-Match` matching the current `ETag` returns `304` with no
  body (the resource already has the ETag from [#12](12-etag-preconditions.md)); this is the
  `If-None-Match` half deferred there. Decide the all-or-nothing conditional-header rule per AEP-154
  (we currently `400` unsupported conditional headers — flip `If-None-Match` to supported).
- Document caching headers behavior; keep it consistent with the etag semantics already shipped.

## Acceptance criteria

- [ ] Responses are compressed when the client accepts it and the body exceeds the threshold.
- [ ] `GET` with a matching `If-None-Match` returns `304` (no body); non-match returns `200`.
- [ ] `If-None-Match` is now supported (no longer `400`); etag semantics stay consistent.

## References

- AEP-154 (preconditions); ASP.NET Core response compression; relates to [#12](12-etag-preconditions.md),
  `docs/KNOWN_ISSUES.md` (the documented `If-None-Match` → 400 caveat to remove)
