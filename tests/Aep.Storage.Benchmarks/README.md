# Storage benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks for the storage layer (#06).
They run against the Docker-free backends (in-memory, SQLite) so they need no external services.

```bash
# everything (takes a few minutes)
dotnet run -c Release --project tests/Aep.Storage.Benchmarks -- --filter '*'

# just the read benchmarks, or just page tokens
dotnet run -c Release --project tests/Aep.Storage.Benchmarks -- --filter '*StoreRead*'
dotnet run -c Release --project tests/Aep.Storage.Benchmarks -- --filter '*PageToken*'

# fast sanity pass (one cold iteration, no statistics)
dotnet run -c Release --project tests/Aep.Storage.Benchmarks -- --filter '*' --job dry
```

Always build/run in **Release** — BenchmarkDotNet refuses a Debug build.

## What's measured

- **`StoreReadBenchmarks`** — point `Get`, first-page `List`, and filtered `List`, swept over:
  - `Backend` = in-memory vs SQLite,
  - `Rows` = 1,000 vs 10,000 seeded rows,
  - `Indexed` = with vs without a declared index on the filtered field.

  The `Indexed` axis shows the **index payoff**: on SQLite, `ListFilteredByAuthor` should drop
  sharply when the `author` index exists (the filter becomes an index seek instead of a scan); on
  the in-memory store the index is a no-op (it always evaluates in-process), which is itself a
  useful contrast.

- **`PageTokenBenchmarks`** — the AES-GCM `Protect`/`Unprotect` codec, i.e. the per-page overhead
  pagination adds (one encrypt per response, one decrypt per continuation).

## Notes

- Postgres/DynamoDB are intentionally excluded — benchmarking them means container latency in the
  numbers and a Docker dependency. Add a backend here if you want to profile it specifically.
- Results are machine-specific; use them for **relative** comparisons (index on/off, backend vs
  backend, row-count scaling), not absolute SLAs.
