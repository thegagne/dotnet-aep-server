# 04 — Reading across collections (AEP-159)

**Theme:** API surface · **Status:** ✅ done

## Delivered

- A parent collection id of `-` (`ResourceDefinition.WildcardCollectionId`) lists across all values
  of that parent — `GET /publishers/-/books`. Routing already matches `-` as a segment value, so
  the wildcard flows through `DirectParentIds` to the store untouched.
- Each backend honors it: SQLite/Postgres **drop the parent-equality predicate** for a wildcard
  parent; in-memory skips it in `ParentsMatch`; **DynamoDB falls back to a `Scan` + `FilterExpression`**
  (a wildcard can't form the GSI partition key) and orders/paginates by id in-process.
- Filter, ordering, pagination, and `skip` all work across the widened set; returned items keep
  their real full resource names (stored `path`). Wildcards compose for grandchildren.
- Tests: 4 end-to-end cases in `ReadAcrossCollectionsTests` (across publishers, filter, pagination,
  grandchild) on the SQLite-backed app, plus a store-level parity test in the InMemory, Postgres,
  and DynamoDB suites (the last exercising the Scan fallback on a real emulator).
- Docs: README "Reading across collections"; caveats (DynamoDB scan cost; direct-parent-only
  scoping) in [docs/KNOWN_ISSUES.md](../KNOWN_ISSUES.md).

## Acceptance criteria

- [x] `GET /publishers/-/books` lists books across all publishers with correct full names.
- [x] Filter, pagination, ordering, and skip all work under a wildcard parent.
- [x] Consistent across in-memory, SQLite, Postgres, and DynamoDB (DynamoDB scan-fallback documented).
- [x] Multi-level wildcards (`/publishers/-/books/-/chapters`) work; direct-parent-only scoping documented.

---

_Original proposal below._

## Summary

Support listing a child resource across all parents using the `-` wildcard collection id,
per AEP-159 — e.g. `GET /publishers/-/books` returns every book regardless of publisher.

## Motivation

A common need is "all books across all publishers" without knowing each `publisher_id`.
AEP-159 standardizes this: a client substitutes `-` for a parent collection id in a List
request, and the server returns matching resources across that collection. Tools and clients
expect this behavior where hierarchies are deep.

## Current state

- List is scoped to a single parent. `IResourceStore.ListAsync(...)` takes
  `IReadOnlyDictionary<string,string> parentIds` and lists within that exact parent scope
  (`src/Aep.Storage.Abstractions/Storage/IResourceStore.cs`).
- Routing binds concrete parent id segments (`src/Aep.AspNetCore/Routing/`); there's no
  handling for a `-` wildcard segment.
- Pagination tokens are resource-bound and encrypted (README → Pagination); a cross-collection
  cursor must remain stable and unforgeable across the wider result set.

## Proposed scope

1. Recognize `-` as a wildcard for one or more parent collection ids in List routes.
2. Extend `ListAsync` (or the parent-scope contract) so a wildcard parent means "any value
   for this parent id" while still honoring filter, pagination, ordering, and skip.
3. Per backend:
   - **SQLite / Postgres**: drop/relax the parent-equality predicate for wildcard segments
     in the generated `WHERE`.
   - **DynamoDB**: a wildcard parent generally can't be a partition-key `Query`; document that
     it falls back to a `Scan` + `FilterExpression`, and how indexes (GSIs) interact.
   - **In-memory**: filter accordingly via the shared `FilterEvaluator`.
4. Ensure returned resource names are full paths (each item carries its real parent ids).
5. Keep page tokens valid and bound to the wildcard query shape.
6. Reflect support in OpenAPI / document the `-` convention.

## Acceptance criteria

- [ ] `GET /publishers/-/books` lists books across all publishers with correct full names.
- [ ] Filter, pagination, ordering, and skip all work under a wildcard parent.
- [ ] Behavior is consistent across in-memory, SQLite, Postgres, and DynamoDB (with the
      DynamoDB scan-fallback documented).
- [ ] Multi-level wildcards (e.g. `/publishers/-/books/-/chapters`) are specified — supported or
      explicitly rejected with a clear error.

## References

- AEP-159 (reading across collections), AEP-132 (List)
- `src/Aep.Storage.Abstractions/Storage/IResourceStore.cs`, `src/Aep.AspNetCore/Routing/`
- README → "Pagination (AEP-158)", "Filtering (AEP-160)"
- Related: [03 — Mark parents NOT IMPLEMENTED](03-mark-parents-not-implemented.md)
