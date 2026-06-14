# 29 — Field masks: partial responses + partial updates (AEP-157/134)

**Theme:** API surface (AEP) · **Status:** proposed

## Summary

Support field masks: a **read mask** to return only requested fields (partial responses, AEP-157),
and a normalized **update mask** for PATCH (which fields to change), so clients move less data and
updates are explicit.

## Scope

- **Read mask** — a `read_mask` (or `fields`) query param on Get/List; the response includes only the
  named fields (+ always the standard `id`/`path`). Validate names against the schema. Note: a true
  storage-side projection (`SELECT` only those columns) is the efficient form; an in-process trim is
  the simple first step.
- **Update mask** — make PATCH's "which fields" explicit via a mask (today PATCH infers it from the
  body keys, which is fine for merge-patch; a mask additionally lets a client clear a field or be
  unambiguous). Reconcile with the merge-patch semantics already shipped.
- Reflect support in OpenAPI; keep conformance green.

## Acceptance criteria

- [ ] `Get`/`List` honor a read mask and return only requested (+ standard) fields; bad field → `400`.
- [ ] PATCH supports an explicit update mask consistent with merge-patch behavior.
- [ ] Read masks optionally push down to a storage projection (documented if in-process only).

## References

- AEP-157 (partial responses / read mask), AEP-134 (update mask); relates to
  [#11](11-merge-patch-content-type.md), [#02](02-aep-compliant-field-types.md)
