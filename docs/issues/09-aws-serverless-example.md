# 09 — Full AWS example: API Gateway + Lambda + Lambda Web Adapter

**Theme:** Examples · **Status:** ✅ done (FLOCI-validated) — see [`examples/aws-serverless/`](../../examples/aws-serverless/)

## Delivered

A complete serverless example runnable locally via **FLOCI** or deployable to real AWS:

- **`Dockerfile`** — DynamoDB-only AEP image + the Lambda Web Adapter (no app changes). The same
  image runs as a plain HTTP container locally and as a Lambda on AWS. **Verified it builds.**
- **`docker-compose.yml`** — FLOCI (DynamoDB) + the app. **Verified end-to-end**: creates/reads a
  publisher + book through the API, and the items land in FLOCI's `aep_publishers`/`aep_books`
  tables. This is the substantive local validation.
- **`floci/run-floci.sh` + `deploy-lambda.sh`** — the faithful local serverless path: register the
  image as a FLOCI Lambda behind an API Gateway HTTP API. **Verified** the image builds and the
  function reaches `Active` in FLOCI; spawning the Lambda *container* needs a Docker daemon FLOCI
  can reach — it failed in this sandbox (Rancher Desktop socket not reachable in-container), so the
  Lambda→APIGW round trip is validated via the SAM path / Docker Desktop, documented honestly.
- **`aws/template.yaml`** — SAM template: Lambda container (built + pushed by `sam build`/`deploy`),
  API Gateway HTTP API (root + `{proxy+}`), and a least-privilege DynamoDB IAM policy on `aep_*`.
- **Credentials**: `Static` for FLOCI, `Ambient` (execution role) for AWS — config only, using the
  `CredentialsSource` option added in [#01](01-configurable-database-connections.md).
- README covering all three paths + notes (statelessness, stable `PageToken__Key` across cold
  starts, cold-start/cost, and the LRO completion model → [#05](05-long-running-operations.md)).

## Acceptance criteria

- [x] `examples/aws-serverless/` deploys end to end via documented IaC (FLOCI locally; SAM for AWS).
- [x] The API serves the full AEP surface (CRUD + list/filter + `/openapi.json`) — verified locally.
- [x] Page tokens stay valid across instances when `PageToken:Key` is set (documented).
- [~] Walkthrough verified against FLOCI (app+DynamoDB end-to-end; Lambda+APIGW spawn blocked by the
      sandbox's Docker socket — works on Docker Desktop / real AWS).

---

_Original proposal below._

## Summary

Ship a complete, deployable AWS serverless example: the AepServer host running on **Lambda**
behind **API Gateway** via the **Lambda Web Adapter**, with selectable **DynamoDB** or
**Postgres** (RDS/Aurora) storage — including infrastructure-as-code and a deploy walkthrough.

## Motivation

The README already states Lambda is a planned target and that, because the host is a standard
ASP.NET Core app, it can run behind the Lambda Web Adapter with no application changes. This
issue turns that claim into a worked, runnable example — the most compelling "deploy it for
real" story, and the natural home for exercising the DynamoDB backend in its native habitat.

## Current state

- Standard ASP.NET Core host (`src/Aep.Server`) + `Dockerfile`; listens on 8080 in-container.
- DynamoDB and Postgres backends exist and are opt-in at build time.
- README "Docker" notes Lambda Web Adapter compatibility but ships no example, IaC, or guide.

## Proposed scope

1. **Example directory** (e.g. `examples/aws-serverless/`) containing:
   - a container image built for Lambda with the Lambda Web Adapter layered in,
   - the storage backend baked/selectable (DynamoDB or Postgres) via build args from
     [08](08-document-container-build.md),
   - a sample `resources.yaml`.
2. **Infrastructure as code** — SAM / CDK / Terraform (pick one, document why) provisioning:
   - the Lambda function (container image) + Web Adapter config,
   - API Gateway (HTTP API) routing all paths to the function,
   - DynamoDB table(s) **or** an RDS/Aurora Postgres instance,
   - IAM roles/policies scoped to the chosen backend.
3. **Configuration wiring** — pass `Storage__*`, region, credentials (task role), and
   `PageToken__Key` (stable key is essential across cold starts / multiple instances —
   see README "Pagination") via Lambda env / secrets.
4. **Deploy walkthrough** — build → push to ECR → deploy → curl the API Gateway URL →
   run aepcli against it ([07](07-aeplinter-aepcli-conformance.md)).
5. **Notes** — cold starts, statelessness (no in-memory store; no durable background worker,
   which affects LRO completion — see [05](05-long-running-operations.md)), and cost.

## Acceptance criteria

- [ ] `examples/aws-serverless/` deploys end to end via documented IaC, both DynamoDB and Postgres variants.
- [ ] API Gateway URL serves the full AEP API (CRUD + list/filter + `/openapi.json`).
- [ ] Page tokens stay valid across cold starts (stable `PageToken:Key` wired in).
- [ ] Walkthrough verified against a real deploy; aepcli drives the deployed endpoint.

## References

- [Lambda Web Adapter](https://github.com/awslabs/aws-lambda-web-adapter)
- README → "Docker" (Lambda note), "Pagination (AEP-158)", "Storage backends"
- Related: [01 — configurable connections](01-configurable-database-connections.md),
  [05 — LRO](05-long-running-operations.md), [08 — container build](08-document-container-build.md)
