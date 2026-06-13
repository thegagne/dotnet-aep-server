# 17 — Observability: OpenTelemetry traces + metrics + a canonical log line

**Theme:** Operability / Production-readiness · **Status:** proposed

## Summary

Add production-grade observability via OpenTelemetry — **intentionally instrumented**, not
auto-instrumented. Three pillars:

1. **One canonical log line per request** — a single structured event that marshals every relevant
   detail of the request/response into one record (no per-step log spam).
2. **Traces** — a small set of deliberate spans at the boundaries that matter, context-propagated.
3. **Metrics** — a deliberate set of RED + storage instruments with bounded cardinality.

## Principles (the "intentional, efficient" part)

- **No auto-instrumentation.** Do **not** pull `OpenTelemetry.Instrumentation.AspNetCore`/`.Http`.
  We define our own `ActivitySource`/`Meter` and instrument exactly the boundaries we care about —
  clean, low-noise, low-cardinality telemetry instead of a span-per-everything firehose.
- **Cheap when unobserved.** Instrument with `System.Diagnostics.ActivitySource` and
  `System.Diagnostics.Metrics.Meter` in the core — these are ~free when no listener/exporter is
  attached. Only the OTel **SDK + exporter** wiring is opt-in (`AddAepObservability(config)`), so a
  no-telemetry deployment carries no weight.
- **Bounded cardinality.** Tag metrics by `resource` (singular), AEP `method`, `status`, `backend`
  — never by resource **id**, full path, filter string, token, **or `client_id`**. Identifying /
  high-cardinality fields (including the caller's client_id) belong on the per-request **log line and
  trace** (per-event, cheap), not on metric labels. Per-client analysis is done by querying the logs/
  traces, not by a metric dimension.
- **Instrument at the chokepoints**, not everywhere: the request boundary and the storage call. The
  architecture already funnels through a single controller + a decoratable `IResourceStore`, so the
  instrumentation has natural seams.
- **Hot-path efficiency.** `LoggerMessage` source-gen for the canonical line; guard attribute
  construction on `ActivitySource.HasListeners()` / sampling; cached `TagList`s; no reflection.

## Current state

- No telemetry. Default ASP.NET `ILogger`; `AepErrorMiddleware` logs unhandled exceptions. No
  `ActivitySource`, `Meter`, traces, metrics, or OTel.

## Design

### 1. Canonical log line (one per request)

A single structured event emitted **once**, at request completion, from a request-scoped accumulator
filled as the request flows. High-signal, queryable, correlated. Fields:

- **correlation** — `trace_id`, `span_id`, `request_id`
- **request** — `http.method`, `route`, `resource` (singular), `aep.method` (`Create`/`List`/…),
  `resource.path`/`id`
- **caller** — `client_id`, granted scopes (from [#16](16-oauth-client-credentials-validator.md));
  anonymous when auth is off
- **result** — `status`, `outcome` (ok/error), `error.type`/`error.code`, `duration_ms`
- **shape** — for List: `filter_present`, `page_size`, `result_count`, `has_next_page`; `backend`,
  `storage_ms`
- **never** tokens or field values

This **subsumes [#16](16-oauth-client-credentials-validator.md)'s basic audit log** (it already
carries client_id + method + path + outcome) — audit becomes a filtered view / durable sink over the
same event rather than a second log.

### 2. Traces (deliberate spans)

- One **`ActivitySource`** (e.g. `Aep.Server`). Spans:
  - **root per request**: `aep.<method> <resource>` (e.g. `aep.create book`) — attributes mirror the
    canonical line's request/caller/result fields; record exceptions + set error status (integrate
    with `AepErrorMiddleware`).
  - **storage child**: one span per store call (`db.operation` = insert/get/list/update/delete,
    `db.system` = sqlite/postgres/dynamodb) via a thin telemetry **decorator over `IResourceStore`**
    — this is where the latency actually goes.
  - Optional, only if they prove interesting: filter translate, page-token crypto, external hook
    calls ([#14](14-resilient-extension-points.md)).
- **Context propagation** (needed even without auto-instrumentation): extract W3C `traceparent`
  inbound; inject it into downstream calls (DynamoDB SDK, external hooks) so traces stitch across
  services.
- **Sampling**: parent-based + configurable ratio for cost control.
- Use OTel **semantic conventions** where they apply (`http.*`, `db.*`) — deliberately, by hand.

### 3. Metrics (deliberate instruments)

One **`Meter`** (`Aep.Server`):

- `aep.requests` (Counter) — by `resource`, `method`, `status` → Rate + Errors.
- `aep.request.duration` (Histogram, ms) — by `resource`, `method` → Duration (RED complete).
- `aep.storage.duration` (Histogram, ms) — by `backend`, `operation`.
- `aep.list.result_count` (Histogram) — page sizes actually returned.
- `aep.requests.in_flight` (UpDownCounter) — concurrency.

**No `client_id` on metrics** — per-client rate/errors/latency is answered by querying the canonical
log line (which carries `client_id`) and traces, keeping metric cardinality bounded. (If a future
need demands per-client *aggregates*, revisit with a capped allowlist — but the log is the source of
truth for per-consumer analysis.)

### 4. Config / export

```yaml
Observability:
  Enabled: true
  ServiceName: "aep-server"          # + service.version, deployment.environment as resource attrs
  Otlp: { Endpoint: "http://collector:4317" }
  Traces: { SampleRatio: 0.1 }
  Console: false                     # dev
```

OTLP exporter to a collector (or console in dev). `AddAepObservability(config)` registers the
`ActivitySource`/`Meter` with the SDK + exporter; absent, the in-core instrumentation is inert.

## Relationships

- **[#16](16-oauth-client-credentials-validator.md)** — the canonical line carries caller identity
  and subsumes the basic audit log; the durable audit sink (if built) reads the same event.
- **[#14](14-resilient-extension-points.md)** — external hook calls get child spans + a hook-latency
  metric; in-process hook timing feeds the canonical line's slow-hook visibility.
- **[#09](09-aws-serverless-example.md)** — under Lambda, prefer flushing via an OTel collector
  extension / async export; document cold-start + export-flush considerations (don't lose spans on
  freeze).

## Open questions

- Where to emit the canonical line + root span — terminal middleware wrapping the pipeline (around
  `AepErrorMiddleware`) vs. an MVC filter? (Middleware catches everything, including error mapping.)
- Default sampling ratio, and head- vs tail-based (tail needs a collector).
- Is the canonical line always on (even with OTel SDK off), or gated by `Observability:Enabled`?
  (Leaning: structured request log always on; traces/metrics export opt-in.)
- Log↔trace correlation format (OTel log records vs. enriching `ILogger` scope with trace ids).

## Acceptance criteria

- [ ] Exactly **one** structured log event per request with the fields above, correlated to its trace.
- [ ] A root span per request + a storage child span per store call, with bounded attributes; errors
      recorded on the span.
- [ ] RED metrics (count/errors/duration) + storage-duration metric, all with bounded-cardinality labels.
- [ ] Inbound `traceparent` is honored and propagated to downstream calls.
- [ ] **No auto-instrumentation packages**; instrumentation is inert (near-zero cost) when no
      exporter is attached; OTLP export is opt-in and configurable.
- [ ] No tokens or field values in any telemetry; metric labels never include ids/filters.

## References

- `System.Diagnostics.ActivitySource` (tracing), `System.Diagnostics.Metrics.Meter` (metrics),
  `LoggerMessage` source-gen; `OpenTelemetry` + `OpenTelemetry.Exporter.OpenTelemetryProtocol`
  (SDK/export, **not** the `Instrumentation.*` auto packages)
- OTel semantic conventions (`http.*`, `db.*`); W3C Trace Context
- `src/Aep.AspNetCore/Http/AepErrorMiddleware.cs`, `Controllers/ResourceController.cs`,
  `src/Aep.Storage.Abstractions/Storage/IResourceStore.cs` (storage telemetry decorator seam)
