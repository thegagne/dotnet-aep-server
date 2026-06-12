# Known issues / intentional quirks

Behaviors that are deliberate (or unavoidable given the ecosystem) and might otherwise look
like bugs. For planned work see [docs/issues/](issues/).

## PATCH advertises `required` fields, but updates are partial

**What you'll see.** In `/openapi.json`, the `PATCH` (Update) request body references the full
resource schema:

```jsonc
"patch": {
  "requestBody": {
    "content": {
      "application/merge-patch+json": { "schema": { "$ref": "#/components/schemas/book" } }
    }
  }
}
```

Because that schema carries the resource's `required` list (e.g. `["title"]`), the spec *appears*
to say a PATCH must include `title`. Tools that read `required` per write verb — notably
[aepcli](https://github.com/aep-dev/aepcli) — will therefore prompt for `--title` on `book update`.

**Why it's this way.** This matches the canonical AEP generator, `aep-lib-go`
(`pkg/api/openapi.go`), which models the Update body as a `$ref` to the resource schema — it does
not emit a separate "patch" schema with `required` stripped. Our generator mirrors aep-lib-go, and
the official `aep-openapi-linter` rule `aep-134-request-body` requires the body to *be* an AEP
resource (carry `x-aep-resource`), which a `$ref` to the resource satisfies. An inline partial
schema (our earlier approach) failed that rule. See
[docs/issues/10-patch-update-body-modeling.md](issues/10-patch-update-body-modeling.md) for the
full analysis and the rejected alternative (a derived patch component, which risks duplicate
`x-aep-resource` annotations confusing resource discovery).

**Actual server behavior is unaffected.** `PATCH` is a true partial update server-side:
`SchemaValidator.ValidateForPatch` does **not** enforce `required`, so omitting `title` on a PATCH
succeeds. Only the *advertised* contract is loose; the runtime does the right thing. (Pass the
field, or use `--@data`, to satisfy aepcli.)

## Reading across collections (AEP-159) — two caveats

**DynamoDB does a full `Scan`.** A wildcard (`-`) parent can't form the `by_parent` GSI partition
key, so `GET /publishers/-/books` falls back to scanning the whole table (filter pushed down as a
`FilterExpression`) and ordering/paginating by id in-process. This reads more than a scoped Query
and costs more on large tables — it's the documented fallback, not a bug. SQLite/Postgres just drop
the parent predicate from the `WHERE`, so they're unaffected.

**A wildcard only relaxes the *direct* parent.** Stores scope a list by a resource's direct
parent id (the column/attribute they actually store). So for a grandchild,
`GET /publishers/p1/books/-/chapters` lists chapters across **all** books — the concrete `p1`
segment is not enforced, because the chapter row only carries `book_id`, not `publisher_id`. Use a
concrete `book_id` to scope to one book, or expect a global-across-books result when the direct
parent is wildcarded.

## `If-None-Match` is rejected with 400

We support the `If-Match` precondition (AEP-154) but not `If-None-Match`. Per AEP-154, an
unsupported conditional header must not be silently ignored, so a request carrying `If-None-Match`
returns `400 INVALID_ARGUMENT`. Supporting it (GET `304`, create-only `*`) is a possible
follow-up — see [docs/issues/12-etag-preconditions.md](issues/12-etag-preconditions.md).
