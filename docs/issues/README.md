# Feature backlog

Tracked features and improvements for `dotnet-aep-server`. Each file is a self-contained
issue: motivation, current state, proposed scope, and acceptance criteria. They're roughly
ordered by how much they build on each other, not strictly by priority.

| # | Issue | Theme |
|---|-------|-------|
| [01](01-configurable-database-connections.md) | Configurable database connections ✅ | Storage |
| [02](02-aep-compliant-field-types.md) | AEP-compliant configurable field types ✅ | Modeling |
| [03](03-mark-parents-not-implemented.md) | Mark root parents "NOT IMPLEMENTED", still serve children ✅ | Routing |
| [04](04-reading-across-collections.md) | Reading across collections (AEP-159) ✅ | API surface |
| [05](05-long-running-operations.md) | Long-running operations (AEP-151) ⏸️ design locked, deferred behind #09 | API surface |
| [06](06-expanded-test-suite-and-performance.md) | Expanded test suite + performance testing ✅ | Quality |
| [07](07-aeplinter-aepcli-conformance.md) | Conformance testing with aeplinter + aepcli | Quality |
| [08](08-document-container-build.md) | Document the container build process ✅ | Docs |
| [09](09-aws-serverless-example.md) | Full AWS example: API Gateway + Lambda + Web Adapter ✅ | Examples |
| [10](10-patch-update-body-modeling.md) | Model the Update (PATCH) body as a resource reference ✅ | Conformance |
| [11](11-merge-patch-content-type.md) | Advertise `application/merge-patch+json` for PATCH ✅ | Conformance |
| [12](12-etag-preconditions.md) | `etag` and preconditions (AEP-154) ✅ | Standard fields |
| [13](13-system-assigned-uid.md) | System-assigned `uid` standard field (AEP-148) ✅ | Standard fields |
| [14](14-resilient-extension-points.md) | Resilient extension points (bound in-process hooks + external handlers) | Extensibility / Reliability |
| [15](15-resource-examples-in-openapi.md) | Example values in `resources.yaml`, surfaced in OpenAPI | Modeling / OpenAPI |

Issues 10–11 came out of building the [#07](07-aeplinter-aepcli-conformance.md) conformance
check — the two linter rules it currently suppresses, each with a concrete path to re-enable.
Issues 12–13 were split out of [#02](02-aep-compliant-field-types.md): standard fields that
need conditional-request handling (12) or storage changes (13) beyond plain field typing.

## Conventions

- One concern per file. Keep cross-references via the relative links above.
- Cite the relevant AEP (e.g. AEP-159) and the concrete files the change touches.
- "Acceptance criteria" should be checkable — a reviewer can tell when the issue is done.
