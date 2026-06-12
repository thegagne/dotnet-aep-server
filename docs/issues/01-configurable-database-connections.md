# 01 — Configurable database connections

**Theme:** Storage · **Status:** ✅ done

## Delivered

- Strongly-typed options per provider, bound from `Storage:<Provider>` and **validated at
  startup** (`.Validate(...).ValidateOnStart()` — bad values fail fast, verified with a clear
  `OptionsValidationException`). Discrete keys layer on top of the connection string.
- **SQLite**: `JournalMode` (validated against the PRAGMA set), `BusyTimeoutMs`, `Pooling` —
  applied via PRAGMA / `SqliteConnectionStringBuilder` (previously hard-coded WAL/5000).
- **Postgres**: `MaxPoolSize`/`MinPoolSize`, `CommandTimeoutSeconds`, `SslMode`, `SearchPath`,
  `ApplicationName` — layered via `NpgsqlConnectionStringBuilder`.
- **DynamoDB**: `CredentialsSource` (Static/Ambient/Profile — Ambient enables task/instance
  roles on AWS, fixing the previous always-static behavior), `Profile`, `BillingMode`
  (PayPerRequest/Provisioned + `Read/WriteCapacityUnits` on table and GSIs), `MaxErrorRetry`,
  `RetryMode`.
- **CLI**: generic `--set key=value` (repeatable) reaches any config key without a flag
  explosion; env vars (`Storage__Postgres__SslMode=…`) work too.
- Tests: per-provider options unit tests (connection-string layering, SSL validity, provisioned
  construction); existing store/integration suites still pass through the new code paths.
- README "Per-provider tuning" tables.

## Acceptance criteria

- [x] Each provider has a bound, validated options type under `Storage:<Provider>`.
- [x] Postgres pool size, command timeout, and SSL mode settable via config + env.
- [x] DynamoDB billing mode and table-name prefix settable via config + env.
- [x] Invalid configuration fails fast at startup with a clear message.
- [x] README configuration tables reflect every new key; new keys covered by tests.

---

_Original proposal below._

## Summary

Make every storage backend fully configurable through `appsettings.json` / environment
variables, rather than exposing only a single connection string (or, for DynamoDB, a
service URL + region). Each provider should surface the connection options operators
actually need in production.

## Motivation

Today the configuration surface (README "Configuration" table) is intentionally thin:

| Setting | What's missing |
|---------|----------------|
| `Storage:Sqlite:ConnectionString` | journal mode, busy timeout, pooling |
| `Storage:Postgres:ConnectionString` | pool min/max, command timeout, TLS/SSL mode, schema/search_path, app name |
| `Storage:DynamoDb:ServiceUrl` + `Region` | credentials profile, table name prefix, read/write capacity vs. on-demand, retry/backoff, custom endpoint per service |

Operators running this beyond local dev need to tune connection pools, timeouts, TLS, and
(for DynamoDB) capacity mode and credentials. Right now those require dropping to a raw
connection string or aren't reachable at all.

## Current state

- Config is bound ad hoc per provider in each `AddAep<Provider>Store(...)` extension
  (`src/Aep.Storage.*/`), reading a flat connection string out of `IConfiguration`.
- The CLI (`src/Aep.Cli`) accepts `--storage` and a single `--connection` value.
- No strongly-typed options class per provider.

## Proposed scope

1. Introduce a strongly-typed options record per provider (e.g. `PostgresStoreOptions`,
   `DynamoDbStoreOptions`, `SqliteStoreOptions`) bound from the `Storage:<Provider>` section
   via the options pattern, with validation on startup (`ValidateOnStart`).
2. Postgres: expose pool sizing, command timeout, SSL mode, search_path/schema, and
   application name — either as discrete keys or as pass-through Npgsql builder properties.
3. DynamoDB: expose credentials source (profile / static / ambient), table name prefix,
   billing mode (on-demand vs. provisioned + RCU/WCU), and retry policy.
4. SQLite: expose journal mode (WAL), busy timeout, and pooling.
5. Keep the single connection string as a shortcut that still works; discrete options layer
   on top of / override it.
6. Extend the CLI so the common knobs are reachable without hand-writing a connection string.
7. Update the README "Configuration" and "Storage backends" tables.

## Acceptance criteria

- [ ] Each provider has a bound, validated options type under `Storage:<Provider>`.
- [ ] Postgres pool size, command timeout, and SSL mode are settable via config + env.
- [ ] DynamoDB billing mode and table-name prefix are settable via config + env.
- [ ] Invalid configuration fails fast at startup with a clear message.
- [ ] README configuration table reflects every new key; new keys covered by tests.

## References

- `src/Aep.Storage.Postgres/`, `src/Aep.Storage.DynamoDb/`, `src/Aep.Storage.Sqlite/`
- README → "Configuration", "Storage backends"
- Related: [09 — AWS serverless example](09-aws-serverless-example.md) (consumes the DynamoDB/Postgres options)
