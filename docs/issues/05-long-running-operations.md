# 05 — Long-running operations (AEP-151)

**Theme:** API surface · **Status:** ⏸️ deferred (spec locked) — build behind [#09](09-aws-serverless-example.md)

## Summary

Let a standard method (`Create`/`Update`/`Delete`, or a custom method) run asynchronously: instead
of blocking, it returns **`202` + an `Operation`** (a durable promise) that the client polls until
`done`, at which point the operation carries the `response` (the resource) or an `error`. This issue
is the **full design**; the build is deferred until there's a concrete async use case and a real
Lambda deploy ([#09](09-aws-serverless-example.md)) to validate the execution/completion path.

## What AEP gives us

- **`Operation` shape (don't invent it):** `path`, `done` (bool), `metadata` (free object),
  and a oneof **`error`** (AEP-193) **or `response`** (the result). Per the JSON-Schema `operation.json`.
- **`/operations` is required:** a service with any LRO **must** expose an `Operation` resource with
  **List + Get** (Get is the poll endpoint).
- **The Operations service is read/control only** — `Get`/`List` (+ optional `Cancel`/`Delete`/`Wait`).
  No public create/update: operations are minted by the method and completed by the server.
- **Standard methods may be LRO** (Create/Update/Delete); `response` **must** be the resource (or an
  empty object for Delete). Reads (Get/List) are **never** LRO.
- **In-flight visibility:** a resource being created/deleted **should** appear in Get/List, marked
  not-usable via a state enum (AEP-216).
- **OpenAPI:** the official **`x-aep-long-running-operation`** extension
  ([aep-components](https://github.com/aep-dev/aep-components) PR #27) names `response_type` and
  `metadata_type` on the operation; success status is **`202`**, body `$ref` → `operation.json`.
- Mandated edges: errors-to-*start* → normal HTTP error; errors-*during* → `operation.error`;
  parallel on a busy resource → `409 ALREADY_EXISTS` (or queue); operations **may** expire (~30 days).

## Locked decisions

1. **`/operations` is a flat, built-in, store-backed resource.** It reuses the existing
   `IResourceStore` (its fields — `done`, `metadata`, `error`, `response` — are JSON-capable), so we
   get Get/List, pagination, **filtering**, all four backends, and TTL/expiry for free. Public
   surface: **Get + List** (+ optional `:cancel` / `Delete`). **No public create/update** — created
   by the LRO trigger, completed by the internal writer.
2. **Async is per-method** (`is_long_running` on a method in `resources.yaml`). A resource can have
   async Create but sync Update, etc.
3. **Operation metadata carries our expansion fields** (`metadata` is the extensible bag):
   `target` (the resource path — the back-link), `method`, `client_id`, `state`, `progress`,
   `create_time`. The `Operation` top-level schema stays exactly AEP's.
4. **Resource → operation is a query, not a field.** The resource carries only **`state`**
   (AEP-216, shared with soft-delete [#26](26-soft-delete-and-undelete.md)); there is **no**
   `operation_path` field. Find the in-flight op with
   `GET /operations?filter=target == "<path>" && done == false`.
5. **Placeholder-on-create.** An async Create writes the resource immediately in `state: CREATING`
   (visible in Get/List, marked not-usable), flips to `ACTIVE` on success, and is removed on failure.
   This keeps `state` uniform across create/update/delete and makes the placeholder row the pending
   payload. (State enum: `ACTIVE` / `CREATING` / `UPDATING` / `DELETING`.)
6. **Status code `202`** for the success response of an async method (per AEP's normative text).
   *Note:* the `x-aep-long-running-operation` example and `aep-lib-go` use `200`; we lead with `202`
   and **verify against the conformance linter** at build time, switching if it requires `200`.
7. **The service owns the write.** Operation state changes only through an internal `IOperations`
   sink (enforces the state machine, reuses etag concurrency [#12](12-etag-preconditions.md)) — never
   a public endpoint, so a client can't forge "my operation succeeded."

## How CRUDL behaves when a method is async

Example: `book` has async **Create/Update/Delete**; **Get/List** sync.

### Create (async)
```
POST /publishers/acme/books?id=1984   {"title":"1984","author":"Orwell"}
```
1. **Sync gate** — validation / duplicate id → `400`/`409` *now* (start-failure, no operation).
2. Mint an operation; write a **placeholder book `CREATING`**; kick off the async work.
3. `202` + Operation `{path:"operations/op_x", done:false, metadata:{state:CREATING, progress:0, target:"publishers/acme/books/1984", method:"create"}}`.
4. **Complete** (internal writer): success → `done:true`, `response` = the book (`ACTIVE`), placeholder flips to `ACTIVE`; failure → `done:true`, `error`, placeholder removed.

### Get (sync, state-aware)
`GET …/books/1984` during the op → `200` with the placeholder (`state:CREATING`); after success →
the book (`state:ACTIVE`); after a create-failure → `404`. Get never returns an Operation.

### List (sync, includes in-flight)
`GET …/books` includes in-flight resources marked with `state`; a client filters
`?filter=state == "ACTIVE"` to exclude them. Operations are their own collection:
`GET /operations?filter=target == "…/books/1984"`.

### Update (async)
`PATCH …/books/1984` — sync gate (`404`/`400`/`412`); mint op; mark the existing book `UPDATING`;
`202` + Operation. Complete → success: `response` = updated book (`ACTIVE`, new values); failure:
`error`, book reverts to `ACTIVE` (old values).

### Delete (async)
`DELETE …/books/1984` — sync gate (`404`/`412`); mint op (`response_type` = empty); mark book
`DELETING`; `202` + Operation. Complete → success: empty `response`, book removed (`GET` → `404`);
failure: `error`, book reverts to `ACTIVE`.

### The `/operations` resource
| | |
|---|---|
| `GET /operations/{op}` | poll one (AEP-required) |
| `GET /operations?filter=…` | list (AEP-required) — filter by `target`/`done`/`method`/`client_id` |
| `POST /operations/{op}:cancel` | *optional* — sets `metadata.state:CANCELLING`; worker stops, completes cancelled |
| `DELETE /operations/{op}` | *optional* — or TTL expiry (~30 days) |
| ~~create / update~~ | never public — trigger creates, internal writer completes |

## Execution & completion (the part deferred to the build)

The standard defines the read/poll contract and is **silent on who writes state** — that's our seam,
and the reason to build behind [#09](09-aws-serverless-example.md):

- `IOperations.StartAsync(...)` (mint, `done:false`), `CompleteAsync(id, response|error)` (the only
  way to finish; enforces transitions), `UpdateMetadataAsync(id, progress)`.
- Fed by **pluggable ingress**, all calling the same sink: an **in-process executor** (hosted
  service), an **authenticated internal HTTP completion hook**, or an **event consumer** (the
  serverless path — the worker is a Lambda; completion is an event). The service owns the store; the
  integrator owns the bus/worker.
- **Cancellation/progress** reach the executor via the same seam (it observes `state:CANCELLING` /
  reports progress through the ingress).

## Cross-cutting

- **Always an Operation, even if fast** — an async method always returns the envelope; if it
  finished instantly the `202` already has `done:true`. Clients check `done` on the first response.
- **Parallel → `409 ALREADY_EXISTS`** by default (detected via the op's `target`); opt into queueing.
- **Idempotent trigger** — a retried trigger with the same key returns the same operation ([#30](30-idempotency-keys.md)).
- **Auth** — the target resource's **read scope** governs reading its operations ([#16](16-oauth-client-credentials-validator.md)).
- **Expiry** — operations purge after a retention window (DynamoDB TTL; SQL sweep), default ~30 days.

## Open / deferred

- `Cancel` and `Wait` (long-poll) are optional — defer to a v2.
- `200` vs `202` — lead with `202`, verify against the conformance linter at build ([#07](07-aeplinter-aepcli-conformance.md)).
- The execution ingress (hook vs event) and its auth — designed against a real workload + [#09](09-aws-serverless-example.md).

## Suggested increments (when unparked)

1. **Read surface:** `Operation` as a built-in store-backed resource + `GET /operations/{id}` + List
   + the `is_long_running` OpenAPI flag (`202` + `x-aep-long-running-operation`). Demoable via an
   async standard Create returning the resource in `response`.
2. **Lifecycle:** `state` field (AEP-216) + placeholder-on-create + mark-on-update/delete; the
   internal `IOperations` sink + state machine; an interceptor can start an operation.
3. **Execution ingress + serverless validation:** the event-consumer / completion-hook seam, proven
   against [#09](09-aws-serverless-example.md).

## Acceptance criteria

- [ ] `/operations` exposes Get + List (built-in, store-backed, filterable); no public create/update.
- [ ] A method marked `is_long_running` returns `202` + an `Operation`; `response` = the resource (empty for Delete).
- [ ] Async Create writes a `CREATING` placeholder visible in Get/List; flips `ACTIVE` / is removed on completion.
- [ ] Async Update/Delete mark the resource `UPDATING`/`DELETING`; revert on failure.
- [ ] Operation state changes only via the internal writer (state machine enforced); survives restart / multi-instance.
- [ ] Parallel mutation on a busy resource → `409`; errors-to-start → HTTP error; errors-during → `operation.error`.
- [ ] OpenAPI advertises `x-aep-long-running-operation` (`response_type`/`metadata_type`); conformance stays green.

## References

- AEP-151 (LRO); `operation.json` / `aep.api.Operation`; `x-aep-long-running-operation` extension
  ([aep-components](https://github.com/aep-dev/aep-components) PR #27, [aeps #293](https://github.com/aep-dev/aeps/issues/293));
  AEP-216 (state)
- `aep-lib-go/pkg/api/openapi.go` (`IsLongRunning`, the extension)
- `src/Aep.AspNetCore/{Controllers/ResourceController.cs,Backend/,OpenApi/OpenApiGenerator.cs}`,
  `src/Aep.Storage.Abstractions/Storage/IResourceStore.cs`
- Relates to: [#09](09-aws-serverless-example.md) (execution ingress), [#12](12-etag-preconditions.md)
  (concurrency), [#16](16-oauth-client-credentials-validator.md) (auth), [#26](26-soft-delete-and-undelete.md)
  (`state`), [#28](28-custom-methods.md) (custom-method LROs), [#30](30-idempotency-keys.md) (idempotent trigger)
