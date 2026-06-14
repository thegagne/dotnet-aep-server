# 28 — Custom methods (AEP-136)

**Theme:** API surface (AEP) · **Status:** proposed

## Summary

Support custom methods — verbs beyond standard CRUD, addressed as `POST .../{id}:verb` (or
collection `:verb`) — per AEP-136, for actions that don't fit Get/List/Create/Update/Delete (e.g.
`:archive`, `:publish`, `:rotate`).

## Scope

- Declare custom methods per resource in `resources.yaml`: name, HTTP verb (usually POST), request/
  response schema, and whether collection- or item-scoped.
- Route + dispatch them through the controller to a handler. Since custom logic is the point, this
  ties directly to the extension model — a custom method's body is an interceptor/external handler
  ([#14](14-resilient-extension-points.md)) or (later) a long-running operation ([#05](05-long-running-operations.md)).
- Emit them in OpenAPI as `:verb` paths with their schemas (aep-lib-go has `XAEPLongRunningOperation`/
  custom-method support to mirror).
- Authorize via scopes ([#16](16-oauth-client-credentials-validator.md)) — custom verb → read or write
  scope (or a declared one).

## Acceptance criteria

- [ ] A resource can declare a custom `:verb` method with request/response schemas, routed and dispatched.
- [ ] It appears correctly in OpenAPI and is scope-authorized.
- [ ] The handler integrates with the extension model (sync now; LRO later).

## References

- AEP-136 (custom methods); `aep-lib-go/pkg/api/openapi.go` (custom-method emission); relates to
  [#14](14-resilient-extension-points.md), [#05](05-long-running-operations.md), [#16](16-oauth-client-credentials-validator.md)
