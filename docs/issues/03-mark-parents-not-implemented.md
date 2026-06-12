# 03 — Mark root parents "NOT IMPLEMENTED" while still serving children

**Theme:** Routing · **Status:** ✅ done

## Delivered

- Declarative form: `not_implemented: true` on a resource (`ResourceDefinition.NotImplemented`,
  parsed by `ServiceDefinitionLoader`). Chosen over relying on an empty `methods: {}` block,
  which is fragile (YAML `methods:` with no value deserializes to "all methods").
- **Routing**: a not-implemented resource maps its collection + item patterns (any verb) to a
  `NotImplemented` controller action that returns **`501`** (AEP-193 body via the error
  middleware). Child routes are registered with their full patterns independently, so they're
  unaffected (`ResourceRouteRegistration`).
- **Parent existence is not verified** — the parent isn't stored here, and creating a child
  never checked parent existence, so children work under any parent id. The id is opaque; this
  is documented as the intended behavior.
- **OpenAPI** omits the not-implemented resource entirely (no schema, no paths, no tag);
  children still reference it by name in their `x-aep-resource.parents`. Verified the generated
  spec is lint-clean (the only finding in a minimal config was unrelated `info-contact`).
- Tests: `NotImplementedParentTests` (children served; 501 on the parent's own GET/POST/GET-item/
  PATCH/DELETE; OpenAPI omission). All backends unaffected (routing-layer change).

## Acceptance criteria

- [x] A resource can be declared so its own standard methods return 501.
- [x] Child resources under that parent are fully served (create/get/list/update/delete).
- [x] OpenAPI omits the unimplemented parent methods, so aepcli/linters agree.
- [x] Behavior for child operations under an unverified parent id is documented and tested.

---

_Original proposal below._

## Summary

Allow a resource to declare that its **own** standard methods are not implemented (return
`501 NOT IMPLEMENTED`, or omit them entirely) while still acting as a routing parent so its
**child** resources remain fully served at their nested paths.

## Motivation

Some hierarchies have a parent that exists only as a namespace/segment in the URL, not as a
resource you can CRUD directly. Example: you want `/publishers/{publisher_id}/books/...` to
work, but `publisher` itself is owned by another system — you don't want to expose
`POST /publishers`, `GET /publishers/{id}`, etc.

Today every resource in `resources.yaml` gets the full method set; there's no way to keep a
parent purely as a path segment while publishing its descendants.

## Current state

- Routing and method registration are driven by each resource's `methods:` block in
  `resources.yaml` and wired through `src/Aep.AspNetCore/Routing/` and
  `src/Aep.AspNetCore/Controllers/ResourceController.cs`.
- The sample (`src/Aep.Server/resources.yaml`) declares `book` and `chapter` with
  `parents:`, and every resource lists a full `methods:` set.
- There is no concept of a "routing-only" / unimplemented parent.

## Proposed scope

1. Decide and document the declarative form. Options:
   - an explicit `implemented: false` (or `methods: {}` interpreted as "none") on the parent, or
   - a per-method opt-out so individual standard methods can be marked unimplemented.
2. When a resource is marked unimplemented:
   - its own collection/item routes either return `501 NOT IMPLEMENTED` (AEP-193 error shape)
     or aren't registered, **but**
   - its path segment is still recognized so child routes resolve, and
   - parent-id values are still validated/required where children need them.
3. Decide whether an unimplemented parent's existence is *verified* (does creating a child
   under a non-existent parent id 404, or is the parent id opaque/unchecked?). Document the
   choice — it likely depends on whether the parent is backed by storage at all.
4. Reflect the absent methods correctly in the OpenAPI spec (don't advertise routes that 501).

## Acceptance criteria

- [ ] A resource can be declared so its own standard methods return 501 (or are unregistered).
- [ ] Child resources under that parent are fully served (create/get/list/update/delete).
- [ ] OpenAPI output omits (or marks) the unimplemented parent methods, so aepcli/linters agree.
- [ ] Behavior for child operations under an unverified parent id is documented and tested.

## References

- `src/Aep.AspNetCore/Routing/`, `src/Aep.AspNetCore/Controllers/ResourceController.cs`
- `src/Aep.AspNetCore/Configuration/ResourceRegistry.cs`, `ServiceDefinitionLoader.cs`
- README → "API surface", error shape (AEP-193)
- Related: [04 — Reading across collections](04-reading-across-collections.md)
