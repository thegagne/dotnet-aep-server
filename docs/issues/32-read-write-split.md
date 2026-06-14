# 32 — Read replicas / read-write split

**Theme:** Performance / Scale · **Status:** proposed

## Summary

Let reads scale independently of writes by routing `Get`/`List` to a read replica and mutations to
the primary, for the relational backends (Postgres) where replicas are common.

## Scope

- Optional second connection (a read endpoint) in the Postgres options
  ([#01](01-configurable-database-connections.md)); route read methods to it, writes to the primary.
- A clean seam: a routing layer over `IResourceStore` (or two `NpgsqlDataSource`s in the store) that
  picks the connection by operation.
- **Replica-lag awareness** — a read immediately after a write may hit a stale replica; offer a
  "read-your-writes" option (route a read to the primary right after a write, or by request hint) and
  document the eventual-consistency tradeoff.
- N/A for DynamoDB (managed replication; eventually-consistent reads are already an option) and
  SQLite (single node) — Postgres-focused.

## Acceptance criteria

- [ ] Reads can be routed to a configured replica; writes always hit the primary.
- [ ] A read-your-writes option exists; lag/consistency behavior is documented.
- [ ] No change when no replica is configured.

## References

- Relates to [#01](01-configurable-database-connections.md), [#23](23-storage-resilience.md);
  seam: `IResourceStore` / Npgsql data sources
