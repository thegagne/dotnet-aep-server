# 08 — Document the container build process

**Theme:** Docs · **Status:** ✅ done (also fixed a broken Dockerfile)

## Delivered

- **Fixed the Dockerfile, which couldn't build as-is:** it `COPY`'d `dotnet-aep-server.sln`
  (the repo uses `.slnx` — no such file) and never copied `Directory.Build.props` (which sets
  `TargetFramework` for every project), so a build failed twice over. Now copies
  `Directory.Build.props` + `src/` and restores/publishes cleanly.
- **Exposed the storage `Include*` flags as Docker build args** (`IncludeSqlite`/`IncludePostgres`/
  `IncludeDynamoDb`), threaded into `dotnet restore`/`publish`, so you can build backend-specific
  images.
- **Verified end to end with real `docker build`/`run`:**
  - default image (295 MB) serves `/openapi.json` and creates resources on SQLite at `/data`;
  - `--build-arg IncludeSqlite=false --build-arg IncludePostgres=true` produces a Postgres-only
    image (264 MB — no native SQLite binding) that contains `Aep.Storage.Postgres.dll` and **no**
    `Aep.Storage.Sqlite.dll`; selecting `provider=sqlite` on it fails fast with
    "unavailable in this build", and it serves on an included provider.
- **README "Docker" rewritten:** two-stage walkthrough, baked-in `resources.yaml`, runtime contract
  (port 8080, `/data` volume, env config), the backend-specific build-arg recipe, image-size
  expectations, and the Lambda/slim-image lead-in to [#09](09-aws-serverless-example.md).

## Acceptance criteria

- [x] README explains every `Dockerfile` stage.
- [x] Build-arg recipe produces a backend-specific image (verified to start with that backend).
- [x] Runtime env vars, port, and volume are documented in one place.
- [x] A slim/Lambda-oriented build recipe is documented and cross-linked.

---

_Original proposal below._

## Summary

Document the container build end to end: how the `Dockerfile` works, how `resources.yaml` is
baked in, which storage backends the image includes, how to toggle them, image size
expectations, data persistence, and how to build a slim image for a specific deployment.

## Motivation

The README "Docker" section shows `docker build` / `docker run` but doesn't explain the build
itself: the multi-stage layout, the `Include*` build args that control which storage providers
ship, how the YAML gets into the image, the exposed port (8080), the `/data` volume, or how to
produce a minimal image (the README notes "publish ≈ 4 DLLs" when SQLite is dropped, but the
container path for that isn't spelled out). This matters for the AWS example
([09](09-aws-serverless-example.md)), which needs a Lambda-friendly image.

## Current state

- `Dockerfile` at repo root; `.dockerignore` present.
- README "Docker" — minimal build/run snippet; baking `resources.yaml` mentioned; Lambda noted
  as a planned target via the Lambda Web Adapter.
- Storage build flags (`IncludeSqlite`, `IncludePostgres`, `IncludeDynamoDb`) documented for
  `dotnet build`/`run` but not in the Docker context.

## Proposed scope

1. Walk through the `Dockerfile` stages (restore/build/publish/runtime) and what each does.
2. Document baking `resources.yaml` (build-time copy) vs. mounting it at runtime, and the
   trade-offs.
3. Show passing the storage `Include*` flags as Docker build args to produce backend-specific
   images (e.g. a DynamoDB-only image with no SQLite native binding).
4. Document the runtime contract: exposed port (8080), `/data` volume for SQLite persistence,
   relevant `Storage__*` / `PageToken__Key` env vars.
5. Note image-size expectations per backend selection.
6. Provide a recipe for a minimal/slim image suited to Lambda (links to [09](09-aws-serverless-example.md)).

## Acceptance criteria

- [ ] README (or a `docs/` page) explains every `Dockerfile` stage.
- [ ] Build-arg recipe produces a backend-specific image (verified to start with that backend).
- [ ] Runtime env vars, port, and volume are documented in one place.
- [ ] A slim/Lambda-oriented build recipe is documented and cross-linked.

## References

- `Dockerfile`, `.dockerignore`, `Directory.Build.props` (the `Include*` flags)
- README → "Docker", "Storage backends"
- Related: [09 — AWS serverless example](09-aws-serverless-example.md), [01 — configurable connections](01-configurable-database-connections.md)
