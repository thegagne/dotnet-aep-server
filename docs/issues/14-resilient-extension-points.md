# 14 — Resilient extension points: bound in-process hooks + registered external handlers

**Theme:** Extensibility / Reliability · **Status:** proposed

## Summary

Custom logic is the escape hatch from "declarative CRUD," but today it runs **in-process and
unbounded** — a slow, hung, or crashing interceptor takes the API server down with it. This issue
hardens the existing in-process hooks *and* adds a safer, isolated alternative: **registered
external handlers** the server calls (with server-owned timeouts, retries, and a circuit breaker),
so heavy or untrusted logic runs in its own failure domain.

## Motivation — custom logic shares the server's failure domain

Interceptors (`OnCreate`/…) and the backend decorator run as `(request, next)` delegates **inside
the request pipeline, in the same process** (`InterceptingResourceBackend`, `ResourceInterceptors`).
There is no timeout, no concurrency bound, and no isolation. A handler that:

- **hangs** (a raw `HttpClient` call to a flaky service with no timeout) ties up the request and,
  at scale, exhausts the thread pool / connections — a single bad hook cascades into an outage;
- **blocks** (sync-over-async, CPU spin) starves the server for *every* request, not just its own;
- **throws** unexpectedly, **leaks**, **deadlocks**, or **OOMs** can corrupt or kill the process.

The blunt truth: **.NET cannot hard-stop arbitrary in-process code.** `CancellationToken` is
*cooperative* — a hook that ignores it (or does blocking work) can't be forcibly abandoned without
killing the thread, which is unsafe. So in-process logic can be made *safer*, but never truly
*isolated*. That asymmetry is the whole argument for an out-of-process option.

## Current state

- `InterceptingResourceBackend` dispatches per-(resource, method) interceptors; `ResourceBackendDecorator`
  wraps everything. Both in-process, both `(request, next) => Task<TResponse>`.
- The request carries a `CancellationToken` (HTTP-scoped), so cooperative cancellation is *possible*,
  but nothing **enforces** a deadline, contains exceptions beyond `AepException` mapping, or limits
  concurrency.

## Two complementary tracks

### Track A — harden the in-process model (best-effort, applies to all hooks)

1. **Per-hook deadline (cooperative).** Wrap each interceptor/decorator call in a linked
   `CancellationTokenSource` with a configurable timeout; on expiry, surface `504`/`503`. Document
   clearly that this only stops *cooperative* code — a hook that ignores the token still runs.
2. **Enforce cancellation propagation** — ensure the request token reaches the hook so client
   disconnect / shutdown cancels cooperative work.
3. **Exception isolation** — catch non-`AepException` failures from custom code, map to a clean
   `500` (or a configured status), and never let a hook corrupt shared state.
4. **Concurrency / resource guards** — optional per-hook concurrency limit (a semaphore) so one
   heavy hook can't consume the whole pool; surface `429`/`503` when saturated.
5. **Observability** — time every hook, log/meter slow ones, so a misbehaving extension is visible.
6. **Guidance** — async-all-the-way, set timeouts on outbound calls, no blocking — documented as the
   contract for in-process hooks.

> Track A makes the *trusted, lightweight, fast* hook safe-ish. It does **not** make an untrusted or
> heavy hook safe — see Track B.

### Track B — registered external handlers (out-of-process isolation)

Instead of arbitrary in-process code, **register an HTTP endpoint** the server calls at an extension
point. The custom logic runs in *its own* process/service (any language; a Lambda, a sidecar, a
microservice); the server owns the call and bounds it.

**Why it's safer:** the handler can't directly hang/crash/leak the API process — only its *HTTP
call* can, and the server controls that with a hard timeout, retries, and a circuit breaker. It's
also **polyglot** and **serverless-native** (a hook is just a Lambda — fits [#09](09-aws-serverless-example.md)).

**Contract — pre/post, not wrapping (transport-agnostic).** You can't pass a `next` continuation
across a process boundary, so external handlers are **pre** and/or **post** hooks; the server runs
the built-in operation between them. The *contract* (the request/response payload below) is the same
regardless of how the handler is reached — only the envelope and auth differ per transport:

- *pre*: server sends the operation context (resource, method, parent ids, fields, identity). The
  handler returns: `continue` (optionally with mutated fields), or `reject` (an AEP-193 error +
  status). Use for validation, authz, enrichment, defaulting.
- *post*: server sends the result; handler returns `continue` (optionally a mutated response) or
  `reject`. Use for notifications, projections, response shaping.

**Transports.** The same contract, delivered by a pluggable transport — pick per hook:

| Transport | How | Auth | Best when |
|-----------|-----|------|-----------|
| `http` | POST to a URL | HMAC-signed / mTLS | portable, any host/language, on-prem |
| `lambda` | AWS SDK `Invoke` by function ARN | **IAM** (no shared secret) | on AWS — no public endpoint to stand up or secure, stays inside the trust boundary |
| `sns` / `eventbridge` *(async only)* | publish an event | IAM | fire-and-forget *post* hooks (notifications) — the [#05](05-long-running-operations.md) event-bus boundary |

**Direct Lambda invoke is often the better external handler on AWS:** the hook is just a function
ARN, authorized by the server's execution role (`lambda:InvokeFunction`) — no API Gateway, no public
URL, no HMAC key to rotate. Sync (`RequestResponse`) invoke gives the response for pre/post;
async (`Event`) invoke is fire-and-forget for notification post-hooks. It reuses the AWS SDK the
DynamoDB backend already pulls (kept opt-in so non-AWS users don't), and — like everything else here
— it's locally testable against **FLOCI**, which emulates Lambda invoke. Async transports (`sns`/
`eventbridge`) only make sense for post-hooks with no response to act on (a post-hook that just
publishes an event *is* the async hand-off from [#05](05-long-running-operations.md)).

**Declarative registration** (on-brand — it lives in `resources.yaml`), per transport:

```yaml
resources:
  book:
    hooks:
      before_create:                              # HTTP webhook (portable)
        transport: http
        endpoint: "https://validator.internal/book/before-create"
        timeout: 2s
        retries: 2            # safe: pre-hooks are idempotent
        on_error: reject      # fail-closed; or `continue` to fail-open
        auth: { type: hmac, secret_ref: "HookSigningKey" }
      after_create:                               # direct Lambda invoke (AWS, IAM auth)
        transport: lambda
        function: "arn:aws:lambda:us-east-1:123:function:book-projector"
        invoke: event         # async fire-and-forget; or `request_response` for a reply
        on_error: continue    # best-effort projection -> fail-open
```

**Server-owned resilience knobs:** per-call **timeout**, **retries + backoff** (with an
**idempotency key** so a retried *post* hook doesn't double-fire side effects — cf. AEP's
`idempotency_key`), a **circuit breaker** (fail fast when the endpoint is down), payload size
limits, and response validation.

**The critical policy — fail-open vs fail-closed, per hook.** If the handler is unreachable, does
the operation **reject** (fail-closed: correct for validation/authz) or **proceed without it**
(fail-open: correct for best-effort enrichment/notification)? This is a correctness-vs-availability
call that *must* be configurable per hook, not global. **Auth** is mandatory (the server
authenticates to the endpoint: HMAC-signed request, mTLS, or IAM for a Lambda) — a hook endpoint
that can reject/mutate operations is a trusted dependency.

## Recommendation

Keep **both**, with clear guidance:

- **In-process interceptors** — for *trusted, lightweight, fast* logic (a stamp, a cheap check, an
  in-memory transform). Hardened by Track A.
- **External handlers** — the recommended path for *heavy, slow, untrusted, polyglot, or
  independently-deployed* logic. The only model that actually isolates the failure domain.

Track A is the immediate defensive win and ships first (it protects today's users). Track B is the
larger, higher-value addition and can land incrementally — likely **`lambda` transport first**
(IAM auth, no endpoint to secure, reuses the AWS SDK + FLOCI we already have, and the serverless
deploy is the prime audience), then `http` for portability, then async (`sns`/`eventbridge`).

## Open questions

- Registration surface: `resources.yaml` (declarative, versioned with the resource) vs. code/config?
  Probably YAML for the contract + code for secrets.
- Do external hooks compose with in-process ones, and in what order (decorator → external-pre →
  interceptors → op → external-post)?
- Idempotency-key source — generate server-side, or require the caller's `Idempotency-Key`?
- Should a pre-hook be able to fully *replace* the operation (return the final resource), or only
  mutate/reject? (Replacement edges toward "custom methods," a separate concern.)
- Transport packaging: keep the `lambda` transport (AWS SDK) in an opt-in package, like the DynamoDB
  backend, so a non-AWS deployment doesn't pull the SDK?

## Acceptance criteria

- [ ] Each in-process hook runs under a configurable (cooperative) deadline; expiry returns a clean
      timeout status; exceptions are contained; slow hooks are observable.
- [ ] An external handler can be registered for a (resource, method) pre/post point over at least
      the `http` and `lambda` transports, called with one transport-agnostic contract; it can mutate
      or reject the operation.
- [ ] External calls are bounded by timeout + retry + circuit breaker, authenticated, and honor a
      per-hook fail-open/fail-closed policy.
- [ ] Docs state plainly which model to use when, and that in-process timeouts are cooperative-only.

## References

- `src/Aep.AspNetCore/Backend/InterceptingResourceBackend.cs`, `ResourceInterceptors.cs`,
  `ResourceBackendDecorator.cs`; README "Custom handler logic"
- Relates to: [#05](05-long-running-operations.md) (async hand-off / the event-bus boundary),
  [#09](09-aws-serverless-example.md) (a hook *is* a Lambda); cf. AEP `idempotency_key`
