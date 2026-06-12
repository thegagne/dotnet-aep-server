# 05 — Long-running operations (AEP-151)

**Theme:** API surface · **Status:** ⏸️ deferred (design locked) — build behind [#09](09-aws-serverless-example.md)

## Summary

Support the AEP-151 long-running operation (LRO) pattern: a method that can't finish
synchronously returns an `Operation` resource (the durable promise) that the client polls until
`done`, at which point it carries a `response` or an `error`.

## Decision: defer the build, lock the design

LRO is **parked, not cancelled.** The design below is settled; the implementation waits until
there's (a) a concrete async use case and (b) a real Lambda deploy ([#09](09-aws-serverless-example.md))
to validate the completion ingress against. Rationale:

- The **synchronous** custom-logic story is already solved — an `OnCreate`/`OnUpdate` interceptor
  (or the backend decorator) can call an external service and block today. LRO adds nothing there.
- The genuinely hard/variable part of async (executing the work, the event bus, retries) is
  **out of scope by design** — it lives in the integrator's infrastructure, not this service.
- So what LRO adds *here* is narrow but real: a standardized `Operation` resource + poll endpoint
  + the service owning operation state. Worth building, but only against a real workload + #09 —
  building the completion ingress (HTTP hook vs. event consumer) and its auth in the abstract
  risks the wrong shape.

## What AEP-151 actually fixes (verified against the spec + `aep.api.Operation`)

- **The `Operation` shape is standard, don't invent it:** `path` (its own resource name, required),
  `done` (bool, required), `metadata` (free object — progress/state live here; there is **no**
  standard progress field), and a oneof **`error`** (AEP-193 status) **or `response`** (the result).
- **The `Operations` service is read/control only:** `GetOperation`, `ListOperations`,
  `WaitOperation`, `CancelOperation`, `DeleteOperation`. There is **no Create and no Update/Apply** —
  operations are not client-created (an LRO method mints one as a side effect) and not
  client-updated (state is server-authoritative).
- **The poll endpoint is mandatory:** `GET /operations/{operation}` — the path, no other params.
- **Standard methods may themselves be long-running:** Create/Update/Delete MAY return an
  `Operation`; then `response` MUST be the normal resource. *So we don't need custom methods to
  support or demo LRO* — flip a flag on a standard method.
- **The spec is deliberately silent on how state is written.** There is no `UpdateOperation` RPC,
  no callback API. The standard defines the read contract + data shape and leaves the *writer*
  undefined — which is exactly the seam we design below.
- Mandated edges: parallel request on a busy resource → queue it or return `409 ALREADY_EXISTS`;
  failure *to start* → normal AEP-193 error; failure *during* → the operation's `error`. "Long" ≈
  10s rule of thumb; operations MAY expire (~30 days). If Create/Delete are LRO, the resource
  SHOULD still appear in Get/List meanwhile, marked unusable via a state enum (AEP-216).

## Locked design

### Principle: the service owns the operations store; it is the sole writer

Nothing reaches around the service to mutate its tables. Owning the write buys: an enforceable
state machine (`running → done`, no un-completing, cancelled-op rules), reuse of our
etag/optimistic-concurrency (#12) so completers can't race, AEP-151 expiry, and zero schema
coupling with workers (swap DynamoDB↔Postgres and workers don't care).

### One internal write API, fed by pluggable ingress

```
IOperations.StartAsync(metadata) -> Operation      // interceptor mints it (done:false)
IOperations.CompleteAsync(id, response | error)    // the only way to finish; enforces transitions
IOperations.UpdateMetadataAsync(id, progress)      // optional progress, written to metadata
```

All writes go through `IOperations` against the operations store. **How completion reaches it** is
pluggable — both adapters call the same `CompleteAsync`:

- **HTTP completion hook** (built-in): an authenticated, internal `POST /internal/operations/{id}:complete`
  taking `{response}` or `{error}`. Any worker that speaks HTTP (Lambda/ECS/external) reports its
  result; *we* write the store. Universal default.
- **Eventing** (integrator-wired): a consumer bound to the integrator's bus (SQS/EventBridge/Kafka)
  that receives completion events and calls `CompleteAsync`. A background `IHostedService` in a
  long-lived host, or the function-invoked-by-event in Lambda. We provide the sink; the bus binding
  is theirs (same way we don't embed their event system today).

### This is NOT a public "PUT /operations"

The public AEP surface stays **read/control only** (`Get/List/Cancel/Wait`). The completion hook is
a *separate control-plane RPC* — not a resource verb, not in `/openapi.json`'s AEP surface,
authenticated to trusted workers only, idempotent, and it enforces the state machine rather than
blind-overwriting. Client `Cancel` is the AEP control method that writes a "cancel-requested" flag
to our store; the worker observes it and completes with a cancelled terminal state (still our write).

### OpenAPI (mechanical — mirror aep-lib-go)

A per-method `is_long_running` flag in `resources.yaml` → `ResourceMethods`. When set, the generator
(matching `aep-lib-go/pkg/api/openapi.go`):
- sets the success response to an external `$ref` → `https://aep.dev/json-schema/type/operation.json`, and
- adds the `x-aep-long-running-operation` extension naming the eventual `response` type (the resource).

(Note: aep-lib-go does **not** emit the `/operations/{id}` path itself — it's a well-known endpoint;
we implement it but don't have to model it.)

## Suggested increments (when unparked)

1. **Readable surface:** `Operation` model + operations store + `GET /operations/{id}` (+List/Cancel/Wait)
   + the OpenAPI `is_long_running` flag. Demoable via a long-running standard Create returning the
   resource in `response`. Self-contained and useful alone.
2. **Write seam:** `IOperations` (start/complete/update with state-machine enforcement) + the
   authenticated HTTP completion hook; an interceptor can start an operation instead of returning a
   resource.
3. **Eventing ingress + serverless validation:** the event-consumer seam, proven against #09's real
   Lambda deploy (this is the part not to design in the abstract).

## Acceptance criteria

- [ ] An `Operation` resource matching `aep.api.Operation` is defined and persisted.
- [ ] An interceptor can start an in-progress operation; polling reflects done + response/error.
- [ ] Operation state is written only via `IOperations` (state machine enforced); survives restart / multi-instance.
- [ ] Completion ingress: built-in authenticated HTTP hook works; event-consumer seam documented.
- [ ] Public surface is read/control only; OpenAPI marks LRO methods and exposes nothing client-writable.

## References

- AEP-151 (long-running operations); `aep.api.Operation` (path/done/metadata/error|response);
  `Operations` service = Get/List/Wait/Cancel/Delete (no create/update)
- `aep-lib-go/pkg/api/openapi.go` (`IsLongRunning`, `x-aep-long-running-operation`, `AEP_OPERATION_REF`)
- `src/Aep.AspNetCore/Backend/`, `src/Aep.AspNetCore/Controllers/ResourceController.cs`,
  `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs`
- Reuse: [#12](12-etag-preconditions.md) (concurrency on operation writes)
- Blocked on / validate with: [#09 — AWS serverless example](09-aws-serverless-example.md)
