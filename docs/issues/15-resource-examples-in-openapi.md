# 15 — Example values in `resources.yaml`, surfaced in OpenAPI

**Theme:** Modeling / OpenAPI · **Status:** proposed

## Summary

Let `resources.yaml` declare **example values** — per field and/or a whole example instance per
resource — and have the OpenAPI generator emit them as `examples` on each property schema, the
resource component schema, and the request/response bodies. Better examples make everything that
reads the spec nicer: aepcli prompts, [ui.aep.dev](https://ui.aep.dev), generated SDK docs, and
Swagger-style UIs all show realistic values instead of blanks.

## Motivation

The generated spec today describes *shape* (type, format, enum, description) but never *values*.
A consumer sees `title: string` with no hint of what a real one looks like. Examples are cheap to
author and pay off across the whole AEP ecosystem that consumes `/openapi.json`. (aepc's resource
model carries examples, but `aep-lib-go`'s OpenAPI writer doesn't emit them — so this is additive.)

## Current state

- `SchemaProperty` carries `Type`/`Format`/`Description`/`Enum`/`Items`/behaviors — **no example**.
- `OpenApiGenerator.PropertySchema` / `BuildResourceSchema` emit none.
- `resources.yaml` has no `example` key (`ServiceDefinitionLoader` would ignore it today).

## Proposed scope

Support two authoring styles (either or both):

```yaml
resources:
  book:
    # (a) a whole-resource example instance
    example:
      title: "1984"
      author: "Orwell"
      price: 1200
      tags: ["dystopia", "classic"]
    schema:
      type: object
      required: ["title"]
      properties:
        title:  { type: string, example: "1984" }          # (b) per-field example
        author: { type: string, example: "Orwell" }
        price:  { type: integer, format: int32, example: 1200 }
        tags:   { type: array, items: { type: string }, example: ["dystopia"] }
```

1. **Per-field `example`** → emit `examples: [<value>]` on that property's schema (OpenAPI 3.1 /
   JSON-Schema 2020-12 uses the `examples` array; a single authored value wraps to one element).
   The value is arbitrary JSON, so **nested objects and arrays** work verbatim.
2. **Resource-level `example`** (a full instance) → emit `examples: [<object>]` on the resource
   **component schema**, and use it as the `example` on the **request body** and **200 response**
   for that resource's operations.
3. **Derive when convenient**: if a resource-level `example` is given but a property has no `example`
   of its own, splice that property's value out of the resource example into its property schema —
   so per-field examples come "for free" from one coherent instance.
4. **Request vs response**: a request-body example should omit **output-only** fields (`id`, `uid`,
   `create_time`, `update_time`, and any `read_only` field); the response example may include them
   (the generator can synthesize plausible values, or just omit them).
5. **Validate at load (fail fast)**: type-check each example against its schema when loading
   `resources.yaml`, so a wrong-typed example is a startup error, not a silently-bad spec. (Reuses
   the same checks as `SchemaValidator`.)
6. **Stay lint-clean**: use the JSON-Schema `examples` array (not the deprecated singular
   `example`) on schema objects, and confirm the AEP OpenAPI linter (the [#07](07-aeplinter-aepcli-conformance.md)
   conformance check) stays green.

## Design details / decisions to make

- **YAML → JSON conversion.** An example value is arbitrary YAML (scalars, maps, sequences);
  it must be converted to a JSON node for emission (and stored on the model as a raw value, not a
  `string`). Decide where that conversion lives (loader) and the model type (e.g. an opaque
  `object?`/`JsonNode`).
- **`example` (schema) vs `examples` (media type).** OpenAPI puts `examples` (array) on *schemas*
  and a different `examples` (map of named examples) on *media types / parameters*. Pick the schema
  form for fields and either form for bodies; be consistent.
- **Precedence** when both a resource-level and a per-field example exist (per-field wins for that
  field; resource example fills the rest).

## Files touched

- `src/Aep.Storage.Abstractions/Model/ResourceSchema.cs` (`SchemaProperty.Example`,
  `ResourceSchema`/`ResourceDefinition` resource-level example)
- `src/Aep.AspNetCore/Configuration/ServiceDefinitionLoader.cs` (parse + YAML→JSON conversion;
  optional load-time validation)
- `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs` (`PropertySchema`, `BuildResourceSchema`,
  request/response bodies)

## Acceptance criteria

- [ ] A per-field `example` appears as `examples` on that property in `/openapi.json`.
- [ ] A resource-level `example` appears on the component schema and the create request / get
      response bodies (request omitting output-only fields).
- [ ] Nested object/array examples round-trip verbatim.
- [ ] A wrong-typed example fails fast at startup with a clear message.
- [ ] The conformance check ([#07](07-aeplinter-aepcli-conformance.md)) stays lint-clean.

## References

- OpenAPI 3.1 / JSON Schema 2020-12 `examples` (array) on schema objects
- `src/Aep.AspNetCore/OpenApi/OpenApiGenerator.cs`; relates to [#02](02-aep-compliant-field-types.md)
  (the schema/property model) and [#07](07-aeplinter-aepcli-conformance.md) (keep the spec lint-clean)
