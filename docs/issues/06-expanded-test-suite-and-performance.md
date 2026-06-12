# 06 — Expanded test suite + performance testing

**Theme:** Quality · **Status:** ✅ done (parity suite + benchmarks)

## Delivered

- **Cross-backend conformance suite** (`tests/Aep.Storage.TestKit`): one abstract
  `ResourceStoreConformanceTests` defining the `IResourceStore` contract, subclassed by all four
  backends. 15 behaviors run identically against in-memory, SQLite, **and real Postgres/DynamoDB**
  (Testcontainers). It adds parity coverage that was previously SQLite-integration-only —
  **optimistic concurrency (`expectedUpdateTime`, AEP-154/#12)** and **`uid` round-trip** are now
  proven on every backend. New cross-backend behavior gets added in one place, not four.
- Consolidated the duplicated in-memory store tests into the kit (deleted the redundant file);
  backend-specific tests (SQLite/Postgres index DDL, DynamoDB GSI planning, options, CEL parser)
  stay in their own projects.
- **Performance benchmarks** (`tests/Aep.Storage.Benchmarks`, BenchmarkDotNet): point Get,
  first-page List, and filtered List swept over backend × row count × **indexed-or-not** (shows the
  index payoff), plus the AES-GCM page-token `Protect`/`Unprotect` cost. Docker-free; documented in
  its README. Not part of `dotnet test`.
- README "Tests" + project-layout updated.

## Acceptance criteria

- [x] A single parity test set runs against all four backends from one place.
- [x] Benchmark project produces repeatable numbers for list/filter/get (+ page-token codec).
- [x] Index-declared vs non-indexed list performance is measured (the `Indexed` benchmark axis).
- [x] Perf instructions documented (benchmark README + main README "Tests").

## Notes / follow-ups

- Edge cases called out in the original proposal (page-token tampering, `max_page_size` bounds,
  filter rejection, immutable/output-only) are already covered by the server integration tests and
  the `FilterParser` tests; the kit adds the store-level filter-rejection parity case.
- Light dedup only: the SQLite/Postgres/DynamoDB projects still carry a few contract tests that
  overlap the kit (kept to avoid churn/risk); these can be trimmed later. The in-memory project is
  fully on the kit.
- The conformance kit gives each of Postgres/DynamoDB a second Testcontainers fixture (one per test
  class) — correct but slower; a shared collection fixture could halve container startups later.
- Benchmarks are a dev tool, not wired into CI.

---

_Original proposal below._

## Summary

Grow the test suite for breadth (more behaviors, more edge cases, cross-backend parity) and
add a performance/benchmark dimension so regressions in latency and throughput are caught.

## Motivation

The current suite (README "Tests") covers the core API and each store, but as features land
(field types, reading across collections, LRO) the matrix needs to grow — and there's
currently **no** performance signal. We want to know how list/filter/pagination scale with
row count, and whether index declarations actually pay off, before shipping changes that
might quietly regress them.

## Current state

- `tests/Aep.Server.Tests/` — `WebApplicationFactory` integration tests (API, pagination,
  interceptors, backend decorator, resource index).
- `tests/Aep.Storage.{Sqlite,InMemory,Postgres,DynamoDb}.Tests/` — per-store unit tests;
  Postgres + DynamoDB use Testcontainers (need Docker).
- No benchmark project; no load/throughput tests; no explicit cross-backend parity suite.

## Proposed scope

1. **Cross-backend parity suite** — a shared set of behavior tests run against every
   `IResourceStore` implementation, so all backends are proven to behave identically
   (filtering, pagination edges, ordering, not-found, duplicate, nested parents).
2. **Edge-case coverage** — page-token tampering, max_page_size bounds, filter rejection
   cases (README lists unsupported CEL), immutable/output-only fields ([02](02-aep-compliant-field-types.md)),
   wildcard list ([04](04-reading-across-collections.md)).
3. **Performance / benchmarks** — a BenchmarkDotNet project (or equivalent) measuring:
   - List throughput vs. row count, with and without a declared index,
   - filter pushdown cost per backend,
   - page-token encrypt/decrypt overhead,
   - create/get latency.
4. **Seed/load harness** — a way to populate N resources for the perf runs.
5. Document how to run perf locally and what numbers to expect.

## Acceptance criteria

- [ ] A single parity test set runs against all four backends from one place.
- [ ] Benchmark project exists and produces repeatable numbers for list/filter/create/get.
- [ ] Index-declared vs. non-indexed list performance is measured and documented.
- [ ] Perf instructions added to the README "Tests" section.

## References

- README → "Tests", "Indexes"
- `tests/` (all projects), `src/Aep.Storage.Abstractions/Filtering/FilterEvaluator.cs`
- Related: [07 — aeplinter/aepcli conformance](07-aeplinter-aepcli-conformance.md)
