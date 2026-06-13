# 16 — Optional OAuth2 client-credentials validator (JWT bearer)

**Theme:** Security / Auth · **Status:** proposed

## Summary

Add **optional** inbound authentication: validate OAuth2 **client-credentials** access tokens
(machine-to-machine) on incoming requests. The server acts purely as a **resource server** — it
*validates* bearer JWTs issued by an external authorization server (Auth0 / Okta / Entra ID /
Cognito / Keycloak / …); it does **not** issue tokens. Off by default (open, as today); when
configured, requests must carry a valid `Authorization: Bearer <jwt>`.

## Motivation

Inbound auth is the unavoidable, orthogonal concern (it's needed regardless of custom logic).
Client-credentials is the right flow for service-to-service APIs like this one — there's no end
user, a calling **service** authenticates with its own credentials and gets a token scoped to what
it may do. We want a vetted, standards-based gate that's secure by default and easy to turn on.

## Current state

- **No auth at all.** Every endpoint is open. Wiring points are `AddAepServer(...)` (services) and
  `MapAepServerAsync(...)` (pipeline) in `src/Aep.AspNetCore/`.

## Design — validate, don't reinvent

Use the framework's vetted JWT bearer stack (`Microsoft.AspNetCore.Authentication.JwtBearer`) with
OIDC discovery — **no hand-rolled crypto or token parsing.**

```yaml
Auth:
  Enabled: true
  Authority: "https://issuer.example.com/"   # OIDC issuer: discovery + JWKS (HTTPS)
  Audience: "aep-api"                          # REQUIRED; must equal the token's aud
  Algorithms: ["RS256", "ES256"]              # asymmetric allowlist (default)
  RequiredScopes: ["aep.access"]              # optional; enforced if set
  ProtectOpenApi: false                        # is /openapi.json public? (default: public for discovery)
```

Flow: caller → IdP token endpoint (client_id + secret, client-credentials grant) → access token →
our API with `Authorization: Bearer`. We verify the token against the IdP's JWKS and authorize by
scope.

### Security requirements (the "needs to be secure" part — these are MUSTs)

- **Signature** verified against the issuer's JWKS, fetched over **HTTPS**, **cached and
  auto-rotated** (refresh on unknown `kid`).
- **Algorithm allowlist** — asymmetric (RS256/ES256) by default; **reject `alg: none`** and
  unexpected algorithms; only allow symmetric (HS*) if an explicit key is configured (discouraged).
- **Validate `iss` and `aud`** (audience match prevents a token minted for another API being
  replayed here), **require `exp`**, honor `nbf`, with a small bounded clock skew.
- **Deny by default** when auth is enabled: no token / invalid token → `401`; valid token without a
  required scope → `403`.
- **Fail closed** — never a "skip validation" dev backdoor that could ship to prod. Local dev turns
  auth *off* (`Enabled: false`), it doesn't bypass a configured validator.
- **No token leakage** — never log tokens; redact the `Authorization` header in logs/traces.
- **TLS assumption** — tokens only over HTTPS; behind a gateway/ALB terminating TLS, honor forwarded
  scheme (`UseForwardedHeaders`) so HTTPS isn't mis-detected.
- **Startup validation** — `Auth:Enabled=true` with no `Authority`/`Audience` fails fast
  (`ValidateOnStart`).

### Authorization — per-resource read/write scopes

A client-credentials token authorizes a **client**, not a person — so authorize by **scope**. Each
resource has a **read** scope (required by `Get`/`List`) and a **write** scope (required by
`Create`/`Update`/`Apply`/`Delete`).

**Scope format** — `<prefix>/<resource-lineage>:<action>`:
- `prefix` — the API identifier (e.g. `accounts`), configured **once** in `Auth:Scopes:Prefix`.
- `<resource-lineage>` — the resource's **full ancestor→self plural chain**, dot-joined (mirrors the
  nesting): e.g. `publishers`, `publishers.books`, `publishers.books.chapters`.
- `:read` / `:write` — the action.

```
accounts/alerts:read            accounts/alerts:write
accounts/alerts.history:read    accounts/alerts.history:write
```

**read and write are independent** — no implication. A read method needs the read scope; a write
method needs the write scope; a client that does both holds both. (`write` does *not* grant read.)

Each resource's two scopes **default** to the derived form, but are **definable per resource**. A
custom scope **omits the prefix** (the server always prepends `<prefix>/`), so the prefix stays a
single source of truth — and a resource can point at a **parent's** scope path this way:

```yaml
Auth:
  Scopes:
    Prefix: "accounts"          # the API namespace; prepended to every scope

resources:
  alert:
    singular: alert
    plural: alerts
    # derived default:  accounts/alerts:read  /  accounts/alerts:write
  history:
    singular: history
    plural: history
    parents: ["alert"]
    # derived default:  accounts/alerts.history:read  /  accounts/alerts.history:write
    scopes:                     # ...or override (prefix-less) — e.g. govern it by the parent's scope
      read:  "alerts:read"      # -> accounts/alerts:read
      write: "alerts:write"     # -> accounts/alerts:write
```

- "API-wide" auth is just the same scope on every resource; there's no separate granularity knob —
  per-resource definition subsumes it.
- Scopes are read from the `scope` (space-delimited) **and** `scp` (array) claims; matching is exact.

### Caller identity & audit (single issuer)

A **single issuer/audience** is supported (multi-tenant is a future extension). The validated
principal is surfaced to the pipeline so custom logic can read *who* is calling without parsing
claims: a **`CallerIdentity`** on the request — `ClientId` (from `client_id` / `azp` / `sub`),
`Scopes`, and the raw `ClaimsPrincipal` (from `HttpContext.User`). When auth is off, it's anonymous.

Every operation is **audit-logged** with the caller and what they did — timestamp, `client_id`,
method (`Create`/…), resource path, and outcome (status). **Tokens and field values are never
logged** (identity + operation metadata only; the `Authorization` header is redacted). A richer,
pluggable audit sink (durable store, success+failure, tamper-evidence, retention) is large enough
to warrant its own follow-up issue.

### Where it sits

Authentication/authorization middleware runs **before** the resource controller, so it gates
*everything*, including custom interceptors — which receive the `CallerIdentity` above (so a hook
can do app-level authz on the client/scopes). `/openapi.json` is public by default (so tooling can
discover the API and its security scheme) but can be protected via `ProtectOpenApi`.

### Advertise it in the spec

When auth is enabled, emit the OpenAPI **security scheme** in the generated spec — an `oauth2`
scheme with the `clientCredentials` flow (token URL from discovery) plus a `security` requirement —
so aepcli / ui.aep.dev / SDK generators know to obtain and send a token.

### Packaging & deployment notes

- Keep `JwtBearer` an **opt-in** dependency (a separate `AddAepAuth(config)` extension, or a small
  package) so a no-auth deployment stays lean — mirrors the opt-in storage backends.
- **Serverless:** on AWS you *could* offload JWT validation to the API Gateway HTTP API's built-in
  JWT authorizer instead of validating in-app. In-app validation is **portable** (works on any
  host); the gateway authorizer is AWS-native. Document both; ship the portable in-app validator.
- **Opaque tokens / revocation:** JWKS/JWT (stateless, scalable) is the default. Token introspection
  (RFC 7662) is an optional alternative for opaque tokens or hard revocation — note as future.

## Decided

- **Scopes**: each resource has an independent **read** and **write** scope, format
  `<prefix>/<plural-lineage>:read|write` where the lineage is the **full ancestor→self plural
  chain** (e.g. `accounts/publishers.books.chapters:read`). `write` does **not** imply read.
  Derived from `Auth:Scopes:Prefix` + nesting; **overridable per resource** in `resources.yaml`,
  where the override is **prefix-less** (the server prepends `<prefix>/`), letting a resource adopt
  a parent's scope path.
- **Single issuer/audience** (multi-tenant deferred).
- **Caller identity** is surfaced to interceptors (`CallerIdentity`) and every operation is
  audit-logged (client_id + method + path + outcome; no tokens/values).
- `/openapi.json` **public** by default (discovery), `ProtectOpenApi` to lock it.

## Open questions

- Audit sink: structured `ILogger` only for v1, with a pluggable durable sink as a follow-up issue?
- Custom-method support later (#05 LRO / custom verbs) — read or write scope, or a third action?

## Acceptance criteria

- [ ] With `Auth:Enabled=false`/absent, behavior is unchanged (open) — it's truly optional.
- [ ] With it enabled, a valid client-credentials JWT (correct `iss`/`aud`/`exp`/signature/alg) is
      accepted; missing/invalid → `401`; missing/insufficient scope → `403`.
- [ ] Reads require a resource's read scope and writes its write scope (independent — `write` does
      not satisfy a read requirement); scopes default to `<prefix>/<full-lineage>:read|write` and can
      be overridden per resource (prefix-less) in `resources.yaml`.
- [ ] JWKS is fetched over HTTPS, cached, and rotates; `alg: none` and non-allowlisted algs are rejected.
- [ ] Misconfiguration (enabled without authority/audience) fails fast at startup; tokens are never logged.
- [ ] Interceptors can read `CallerIdentity` (client_id, scopes); each operation is audit-logged.
- [ ] The generated OpenAPI advertises the `oauth2` client-credentials security scheme when enabled.

## References

- OAuth 2.0 client-credentials (RFC 6749 §4.4); JWT (RFC 7519); JWKS (RFC 7517); introspection (RFC 7662)
- `Microsoft.AspNetCore.Authentication.JwtBearer`; `src/Aep.AspNetCore/AepServerServiceCollectionExtensions.cs`,
  `AepServerApplicationBuilderExtensions.cs`
- Relates to: [#09](09-aws-serverless-example.md) (gateway-vs-app validation), [#14](14-resilient-extension-points.md)
  (the inbound-auth boundary; surfacing identity to hooks)
