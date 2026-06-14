# Feature backlog

Tracked features and improvements for `dotnet-aep-server`. Each file is a self-contained issue:
motivation, current state, proposed scope, and acceptance criteria.

**Status:** ✅ shipped · ✍️ proposed (designed, not built) · ⏸️ deferred (design locked).

The AEP **core is complete** — the server is conformant to zero linter deviations, with all
standard methods, field types/behaviors, filtering, pagination, preconditions, standard fields, and
cross-collection reads, over four pluggable backends, plus a cross-backend test suite, benchmarks,
container build, and an AWS-serverless example. What remains (the **Proposed** list) is
production hardening (security, reliability, ops), more of the AEP method surface (batch, custom
methods, soft delete, field masks, idempotency), performance, and DX polish.

## ✍️ Proposed — open work

Grouped by intent.

**Security**
| # | Issue |
|---|-------|
| [16](16-oauth-client-credentials-validator.md) | Optional OAuth2 client-credentials validator (JWT bearer) |
| [18](18-rate-limiting.md) | Rate limiting / throttling (per-client / per-IP) |
| [19](19-input-validation-and-request-limits.md) | Stronger input validation + request limits |
| [20](20-secrets-from-a-vault.md) | Load secrets from a vault |
| [21](21-durable-audit-log.md) | Durable audit log |

**Reliability & operability**
| # | Issue |
|---|-------|
| [17](17-observability-opentelemetry.md) | Observability — OpenTelemetry traces + metrics + canonical log line |
| [22](22-health-checks-and-graceful-shutdown.md) | Health checks + graceful shutdown |
| [23](23-storage-resilience.md) | Storage resilience — retries, backoff, circuit breaker, timeouts |
| [24](24-ci-for-the-test-suite.md) | CI for the .NET test suite |
| [25](25-container-image-hardening.md) | Container image hardening (minimal base, scan, SBOM) |

**API completeness (AEP standard methods)**
| # | Issue |
|---|-------|
| [26](26-soft-delete-and-undelete.md) | Soft delete + undelete (AEP-164) |
| [27](27-batch-methods.md) | Batch methods (AEP-231/233/234/235) |
| [28](28-custom-methods.md) | Custom methods (AEP-136) |
| [29](29-field-masks.md) | Field masks — partial responses + update masks (AEP-157/134) |
| [30](30-idempotency-keys.md) | Idempotency keys (AEP-155) |

**Performance**
| # | Issue |
|---|-------|
| [31](31-http-response-efficiency.md) | HTTP efficiency — compression + ETag conditional GET (`If-None-Match`) |
| [32](32-read-write-split.md) | Read replicas / read-write split |

**Modeling / DX**
| # | Issue |
|---|-------|
| [15](15-resource-examples-in-openapi.md) | Example values in `resources.yaml`, surfaced in OpenAPI |

**Extensibility** (larger design; lower priority given the DB-only lean)
| # | Issue |
|---|-------|
| [14](14-resilient-extension-points.md) | Resilient extension points — bound in-process hooks + external handlers |

## ⏸️ Deferred

| # | Issue | Why |
|---|-------|-----|
| [05](05-long-running-operations.md) | Long-running operations (AEP-151) | **Full spec written** (flat `/operations`, `202`+Operation, placeholder-on-create, store-owned writer); build behind [#09](09-aws-serverless-example.md). |

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
- **18–32** came from a "what makes this production-grade" sweep — security, reliability/ops,
  remaining AEP methods, and performance. (CORS/HSTS, DB migrations, Helm, and delivery/publishing
  were intentionally left out of scope for now.)

## Conventions

- One concern per file; cross-reference with the relative links above.
- Cite the relevant AEP (e.g. AEP-159) and the concrete files the change touches.
- "Acceptance criteria" should be checkable — a reviewer can tell when the issue is done.
- Mark status in the issue's header **and** the table above when it changes.
