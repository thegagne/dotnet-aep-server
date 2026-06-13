# Feature backlog

Tracked features and improvements for `dotnet-aep-server`. Each file is a self-contained issue:
motivation, current state, proposed scope, and acceptance criteria.

**Status:** ✅ shipped · ✍️ proposed (designed, not built) · ⏸️ deferred (design locked).

The AEP **core is complete** — the server is conformant to zero linter deviations, with all
standard methods, field types/behaviors, filtering, pagination, preconditions, standard fields, and
cross-collection reads, over four pluggable backends, plus a cross-backend test suite, benchmarks,
container build, and an AWS-serverless example. What remains is production hardening, DX polish, and
two larger designs.

## ✍️ Proposed — open work

Roughly in suggested order; grouped by intent.

**Production-readiness**
| # | Issue | Theme |
|---|-------|-------|
| [16](16-oauth-client-credentials-validator.md) | Optional OAuth2 client-credentials validator (JWT bearer) | Security / Auth |
| [17](17-observability-opentelemetry.md) | Observability — OpenTelemetry traces + metrics + canonical log line | Operability |

**Modeling / developer experience**
| # | Issue | Theme |
|---|-------|-------|
| [15](15-resource-examples-in-openapi.md) | Example values in `resources.yaml`, surfaced in OpenAPI | Modeling / OpenAPI |

**Extensibility (larger design; lower priority given the DB-only lean)**
| # | Issue | Theme |
|---|-------|-------|
| [14](14-resilient-extension-points.md) | Resilient extension points — bound in-process hooks + external handlers | Extensibility / Reliability |

## ⏸️ Deferred

| # | Issue | Why |
|---|-------|-----|
| [05](05-long-running-operations.md) | Long-running operations (AEP-151) | Design locked; build behind [#09](09-aws-serverless-example.md) once a concrete async use case exists. |

## ✅ Shipped

**AEP conformance & spec**
| # | Issue |
|---|-------|
| [07](07-aeplinter-aepcli-conformance.md) | Conformance testing with aeplinter + aepcli (the harness; → zero deviations) |
| [10](10-patch-update-body-modeling.md) | Model the Update (PATCH) body as a resource reference |
| [11](11-merge-patch-content-type.md) | Advertise `application/merge-patch+json` for PATCH |

**Field types & standard fields**
| # | Issue |
|---|-------|
| [02](02-aep-compliant-field-types.md) | AEP-compliant configurable field types + behaviors |
| [12](12-etag-preconditions.md) | `etag` and preconditions (AEP-154) |
| [13](13-system-assigned-uid.md) | System-assigned `uid` standard field (AEP-148) |

**API surface**
| # | Issue |
|---|-------|
| [03](03-mark-parents-not-implemented.md) | Mark root parents "NOT IMPLEMENTED", still serve children |
| [04](04-reading-across-collections.md) | Reading across collections (AEP-159) |

**Storage · Quality · Packaging**
| # | Issue |
|---|-------|
| [01](01-configurable-database-connections.md) | Configurable database connections |
| [06](06-expanded-test-suite-and-performance.md) | Cross-backend conformance suite + benchmarks |
| [08](08-document-container-build.md) | Document (and fix) the container build |
| [09](09-aws-serverless-example.md) | AWS serverless example: API Gateway + Lambda + DynamoDB (FLOCI-validated) |

## How these came about

- **10–11** fell out of building the [#07](07-aeplinter-aepcli-conformance.md) conformance check —
  the two linter rules it suppressed, each re-enabled.
- **12–13** were split out of [#02](02-aep-compliant-field-types.md): standard fields needing
  conditional-request handling (12) or storage changes (13) beyond plain field typing.
- **14–17** came from the production-readiness discussion (extension-point safety, auth, telemetry)
  and a DX request (examples). #14's external-handler model and #05's async completion share the
  event-bus boundary.

## Conventions

- One concern per file; cross-reference with the relative links above.
- Cite the relevant AEP (e.g. AEP-159) and the concrete files the change touches.
- "Acceptance criteria" should be checkable — a reviewer can tell when the issue is done.
- Mark status in the issue's header **and** the table above when it changes.
