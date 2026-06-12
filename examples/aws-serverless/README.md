# AEP server — AWS serverless example

The AEP server packaged as **AWS Lambda** (a container image + the
[Lambda Web Adapter](https://github.com/awslabs/aws-lambda-web-adapter), no application changes)
behind an **API Gateway HTTP API**, backed by **DynamoDB**. You can run the whole thing **locally
against [FLOCI](https://github.com/hectorvent/floci)** (a fast local AWS emulator) or deploy it to
real AWS with SAM.

```
client ──HTTP──▶ API Gateway (HTTP API) ──▶ Lambda (AEP image + Web Adapter) ──▶ DynamoDB
                                              the unmodified ASP.NET app on :8080
```

## Files

| File | What it is |
|------|-----------|
| [`Dockerfile`](Dockerfile) | DynamoDB-only AEP image + Lambda Web Adapter (the Lambda artifact) |
| [`resources.yaml`](resources.yaml) | the service definition baked into the image |
| [`docker-compose.yml`](docker-compose.yml) | **local dev loop** — FLOCI (DynamoDB) + the app |
| [`floci/run-floci.sh`](floci/run-floci.sh), [`floci/deploy-lambda.sh`](floci/deploy-lambda.sh) | **local serverless** — deploy as a FLOCI Lambda + API Gateway |
| [`aws/template.yaml`](aws/template.yaml) | **real AWS** — SAM template (Lambda container + HTTP API + DynamoDB + IAM) |

The same image is used everywhere; only **credentials** differ — `Static` (AccessKey/SecretKey)
for the FLOCI emulator, `Ambient` (the function's execution role) on AWS. That switch is config
only, thanks to the DynamoDB `CredentialsSource` option (see the main README "Per-provider tuning").

## 1. Local dev loop — FLOCI DynamoDB (✅ the quick path)

Runs the Lambda image as a plain HTTP container (the Web Adapter is inert outside the Lambda
runtime) against FLOCI's DynamoDB. Fastest way to exercise the DynamoDB-backed app.

```bash
docker compose -f examples/aws-serverless/docker-compose.yml up --build

curl localhost:8080/openapi.json
curl -X POST 'localhost:8080/publishers?id=acme' -H 'Content-Type: application/json' -d '{"display_name":"Acme"}'
curl -X POST 'localhost:8080/publishers/acme/books?id=1984' -H 'Content-Type: application/json' \
     -d '{"title":"1984","author":"Orwell","price":1200}'
curl localhost:8080/publishers/acme/books/1984

# the data really is in FLOCI's DynamoDB:
AWS_ACCESS_KEY_ID=local AWS_SECRET_ACCESS_KEY=local AWS_DEFAULT_REGION=us-east-1 \
  aws --endpoint-url http://localhost:4566 dynamodb list-tables
```

The app creates its tables (`aep_publishers`, `aep_books`) on startup via `EnsureSchema`.

## 2. Local serverless — FLOCI Lambda + API Gateway

The full topology, locally: the image as a FLOCI Lambda fronted by an API Gateway HTTP API.
FLOCI runs Lambda **container** images by spawning them on a Docker daemon, so it needs the host
Docker socket.

```bash
examples/aws-serverless/floci/run-floci.sh      # FLOCI with the Docker socket mounted
examples/aws-serverless/floci/deploy-lambda.sh  # build image, create Lambda + HTTP API, print URL
```

**Heads-up (environment-dependent).** FLOCI launches the Lambda by talking to the Docker daemon
behind the socket you mount. This works on Docker Desktop; on some setups (e.g. **Rancher
Desktop**, where the daemon socket isn't reachable from inside a container) FLOCI registers the
function as `Active` but fails to spawn the container (`Failed to start Lambda container:
Connection refused`). FLOCI also expects the image to be available to *its* daemon — load/push it
if your daemon and FLOCI's differ. If you hit this, use path 1 for local validation and path 3 for
the real Lambda + API Gateway round trip. (Verified here: the image builds, the function reaches
`Active`, and the app+DynamoDB path works end-to-end via path 1.)

## 3. Real AWS — SAM

```bash
cd examples/aws-serverless/aws
sam build            # builds the Lambda image from ../Dockerfile
sam deploy --guided  # creates the ECR repo, pushes the image, deploys the stack
# -> Outputs.ApiUrl is your API Gateway base URL; curl it like the localhost examples above.
```

The template grants the function a least-privilege DynamoDB policy on `aep_*` tables and sets
`CredentialsSource=Ambient` so the SDK uses the execution role — no static keys in the image.

## Notes

- **Stateless by design.** No SQLite/in-memory state — every instance/cold-start talks to DynamoDB,
  so it scales horizontally and survives Lambda freezes.
- **Page tokens across cold starts.** List pagination tokens are signed with a key. Set a stable
  `PageToken__Key` (env / Secrets Manager) so tokens stay valid across instances — otherwise each
  cold start uses a fresh random key and old `page_token`s break. See the main README "Pagination".
- **Cold starts & cost.** First request per instance pays JIT + (first ever) table creation; tune
  `MemorySize`. DynamoDB defaults to on-demand billing (`BillingMode=PayPerRequest`).
- **Long-running operations** would complete out-of-band in this model (no durable worker in
  Lambda) — see the design in [docs/issues/05](../../docs/issues/05-long-running-operations.md).
