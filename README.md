# dotnet-aep-server

A .NET API server that turns a static [`resources.yaml`](src/Aep.Server/resources.yaml)
into a fully functional, [AEP](https://aep.dev)-compliant REST API on **ASP.NET Core**.
Resources are fixed at build time (declared in YAML, not created at runtime).

- **YAML-driven** — declare resources, schemas, nesting, and methods in the aepc service-definition format.
- **AEP standard methods** — Get, List (pagination + filtering), Create, Update (PATCH), Apply (PUT), Delete.
- **Nested resources** — parent/child paths like `/publishers/{publisher_id}/books/{book_id}`.
- **CEL filtering** — list filters use a dependency-free [CEL](https://github.com/google/cel-spec) subset (AEP-160).
- **Pluggable storage** — SQLite (default), zero-dependency in-memory, PostgreSQL, or DynamoDB — each opt-in so you ship only what you use.
- **Extensible** — add custom logic with per-method interceptors or a global backend decorator (operation middleware).
- **Run it your way** — embed as a library, run the `dotnet` tool, or deploy the Docker image.
- **OpenAPI 3.1** — an AEP-annotated spec at `/openapi.json`, compatible with [aepcli](https://github.com/aep-dev/aepcli) and [ui.aep.dev](https://ui.aep.dev).

## Why

Most of an API is the same plumbing — CRUD over a datastore, pagination, filtering, validation,
concurrency, errors — repeated for every resource. [AEP](https://aep.dev) says how to do all of it
*correctly and consistently*, but hand-implementing the full standard per resource is a mountain of
fiddly, easy-to-get-subtly-wrong boilerplate. And that boilerplate had a Go toolchain (aepc,
aep-lib-go, aepbase) but no **.NET** equivalent.

This collapses it to a single `resources.yaml`. You declare your resources; it serves a
**genuinely conformant** AEP API — verified against the official OpenAPI linter and aepcli down to
zero deviations — so the rest of the AEP ecosystem (aepcli, [ui.aep.dev](https://ui.aep.dev),
generated SDKs, linters) works against your API for free, not approximately.

The goal is to make the boring 80% disappear so your effort goes to the logic that's actually
yours. Simple resources are pure datastore CRUD, handled here; anything custom — call a service,
enforce a rule, hand off external or async work — bolts on through interceptors. Pick your storage,
ship only what you use, and run it as a library, a CLI, a container, or a Lambda.

## How you use it

You write a [`resources.yaml`](src/Aep.Server/resources.yaml) describing your resources; AepServer
serves them as an AEP REST API with an OpenAPI spec. Run it whichever way fits:

- **CLI** — `aep serve resources.yaml` — no code (see [Run via the CLI tool](#run-via-the-cli-tool)).
- **Library** — `AddAepServer()` inside your own ASP.NET Core app (see [Embed in your app](#embed-in-your-aspnet-core-app)).
- **Docker** — bake the YAML into an image (see [Docker](#docker)).

> **Status:** the `AepServer.*` packages aren't published to nuget.org yet, so
> `dotnet tool install`/`dotnet add package` won't resolve them remotely. Until then, build
> them from this repo with `dotnet pack -c Release` (see [Packages](#packages)), or just run
> the sample below.

## Quick start (run the sample)

The repo ships a sample `resources.yaml` (a `publisher → book → chapter` hierarchy). Clone and run it:

```bash
git clone https://github.com/thegagne/dotnet-aep-server && cd dotnet-aep-server
dotnet run --project src/Aep.Server     # serves the sample on http://localhost:5268 (Docker uses 8080)
```

Then exercise it — edit [`src/Aep.Server/resources.yaml`](src/Aep.Server/resources.yaml) and re-run to serve your own resources:

```bash
# create a publisher, then a book nested under it
curl -X POST 'http://localhost:5268/publishers?id=acme' \
  -H 'Content-Type: application/json' -d '{"display_name":"Acme Press"}'
curl -X POST 'http://localhost:5268/publishers/acme/books?id=1984' \
  -H 'Content-Type: application/json' \
  -d '{"title":"1984","author":"Orwell","price":1200,"published":true,"tags":["dystopia"]}'

curl 'http://localhost:5268/publishers/acme/books/1984'                       # get
curl 'http://localhost:5268/publishers/acme/books'                            # list
curl 'http://localhost:5268/publishers/acme/books?filter=author%3D%3D%22Orwell%22'  # filter: author == "Orwell"
curl 'http://localhost:5268/openapi.json'                                     # OpenAPI 3.1 spec
```

## Packages

The project ships as a small family of NuGet packages, so you can embed AEP into
your own app or run it as a tool — not just as a container.

| Package | What it is |
|---------|-----------|
| `AepServer.AspNetCore` | Embeddable server library (`AddAepServer` / `MapAepServerAsync`) |
| `AepServer.Storage.Abstractions` | Storage contracts, resource model, CEL filter parser/evaluator |
| `AepServer.Storage.Sqlite` | SQLite backend (`AddAepSqliteStore`) |
| `AepServer.Storage.InMemory` | Zero-dependency in-memory backend (`AddAepInMemoryStore`) |
| `AepServer.Storage.Postgres` | PostgreSQL backend (`AddAepPostgresStore`) |
| `AepServer.Storage.DynamoDb` | DynamoDB backend (`AddAepDynamoDbStore`) |
| `AepServer.Cli` | `dotnet tool` that serves a `resources.yaml` (`aep serve`) |

### Embed in your ASP.NET Core app

```csharp
// dotnet add package AepServer.AspNetCore AepServer.Storage.Sqlite
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAepServer(builder.Configuration)   // loads resources.yaml (Resources:File)
    .AddAepSqliteStore(builder.Configuration);   // or .AddAepInMemoryStore()

var app = builder.Build();
await app.MapAepServerAsync();   // ensures schema, maps routes + /openapi.json
app.Run();
```

`AddAepServer` also has an overload taking a `ServiceDefinition` if you build the
resource model in code instead of YAML. Storage is chosen by which provider package
you reference and call — reference only `AepServer.Storage.InMemory` and you ship no
native SQLite binding.

### Run via the CLI tool

Once published this is just `dotnet tool install -g AepServer.Cli`. Until then, build and
install it from a local pack:

```bash
dotnet pack src/Aep.Cli -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg AepServer.Cli

aep serve ./resources.yaml --storage sqlite --urls http://localhost:8080
# --storage inmemory | sqlite,  --connection <sqlite cs>,  --urls <listen urls>
```

### Custom handler logic

Two extension points — both the middleware pattern at the operation level. Pick the
narrowest that fits: a per-(resource, method) interceptor, or a global decorator.

#### Per-method interceptors (one resource, one method)

The common case — "do X on **book Create**, and nothing else." Register a handler for an
exact (resource, method) pair. It receives the request and a `next` delegate (the built-in
operation); call `next` to run it (before/after your logic), or skip it to replace the
operation entirely.

```csharp
builder.Services.AddAepServer(builder.Configuration).AddAepSqliteStore(builder.Configuration);

builder.Services.OnCreate("book", async (request, next) =>
{
    var response = await next(request);                 // run the built-in create
    await request.Services.GetRequiredService<IEventBus>()
        .PublishAsync("book.created", response.Resource);
    return response;                                    // ...only for book Create
});
```

`OnCreate` / `OnGet` / `OnList` / `OnUpdate` / `OnApply` / `OnDelete` each take `(singular, handler)`.
Multiple handlers for the same pair compose (first registered is outermost). Resolve services
via `request.Services`.

#### Backend decorator (cross-cutting, all resources)

When you want one wrapper across **every** resource/method — logging, tenancy, a uniform
transaction — decorate [`IResourceBackend`](src/Aep.AspNetCore/Backend/IResourceBackend.cs).
Your type *is* the backend and decides whether/how to call the inner one. Subclass
[`ResourceBackendDecorator`](src/Aep.AspNetCore/Backend/ResourceBackendDecorator.cs) so
unoverridden methods pass through:

```csharp
builder.Services.DecorateResourceBackend<LoggingBackend>();   // stacks if called repeatedly

sealed class LoggingBackend(IResourceBackend backend) : ResourceBackendDecorator(backend)
{
    public override async Task<CreateResponse> CreateAsync(CreateRequest request)
    {
        var response = await Backend.CreateAsync(request);   // call the wrapped backend...
        // ...or skip it entirely and return your own CreateResponse
        return response;
    }
}
```

This is the direct analogue of wrapping a `ResourceBackend` interface. It runs for all
resources (branch on `request.Resource.Singular` to scope), and sits *outside* the per-method
interceptors.

In both, throw an `AepException` (e.g. `AepStatusException(403, "...")`) to abort with a
status, or mutate the response before returning it (e.g. `response.Resource?.Fields.Remove("secret")`).

## API surface

For each resource (e.g. `book` nested under `publisher`):

| Method | Path | Description |
|--------|------|-------------|
| `POST`   | `/publishers/{publisher_id}/books`            | Create (`?id=` sets the id) |
| `GET`    | `/publishers/{publisher_id}/books`            | List (`?max_page_size=`, `?page_token=`, `?filter=`, `?skip=`) |
| `GET`    | `/publishers/{publisher_id}/books/{book_id}`  | Get |
| `PATCH`  | `/publishers/{publisher_id}/books/{book_id}`  | Update (merge) |
| `PUT`    | `/publishers/{publisher_id}/books/{book_id}`  | Apply (create-or-replace) |
| `DELETE` | `/publishers/{publisher_id}/books/{book_id}`  | Delete |

Errors use the AEP-193 shape — RFC 9457 Problem Details served as `application/problem+json`:
`{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "status": 404, "title": "Not Found", "detail": "...", "instance": "/publishers/.../books/..." }`.

### Filtering (AEP-160, CEL)

List filters use [CEL](https://github.com/google/cel-spec) syntax. Rather than take
on a full CEL engine (and its heavy dependency tree), we hand-roll a small,
dependency-free parser for a **strict subset** of CEL — so every filter accepted
here is also valid under a real CEL engine (forward-compatible). Per AEP-160's
"may support a subset" allowance, the supported subset is:

- comparisons `== != < <= > >=` between a **field and a literal** (string / int / double / bool)
- combined with `&&` and `||` and parentheses

```
?filter=author == "Orwell" && price > 1000
?filter=(author == "Orwell" || author == "Huxley") && published == true
```

Not supported (return `400` / INVALID_ARGUMENT): functions/macros
(`startsWith`, `has`, …), `in`, arithmetic, unary `!`, and field-to-field
comparisons. SQL-style `=` and `AND`/`OR` are **not** CEL and are rejected.
Internally the CEL AST is lowered to a provider-agnostic filter tree, which the
SQLite provider translates to a parameterized `WHERE` clause.

### Pagination (AEP-158)

List returns up to `max_page_size` results plus a `next_page_token`; pass it back as
`page_token` for the next page. Tokens are **opaque and unforgeable** — each is an
AES-GCM-encrypted, resource-bound cursor, so it exposes nothing about the data and can't
be crafted or tampered with. A forged or tampered token returns `400`, as does a negative
`max_page_size` (`0` or absent uses the default).

Set a stable base64 32-byte key via `PageToken:Key` (env `PageToken__Key`) to keep tokens
valid across restarts and multiple instances; otherwise a random per-process key is used.

```bash
PageToken__Key=$(head -c 32 /dev/urandom | base64)
```

### Preconditions / optimistic concurrency (AEP-154)

Every single-resource response carries an `ETag` header — an opaque tag that changes whenever
the resource changes. Send it back as `If-Match` on a later `GET`/`PATCH`/`PUT`/`DELETE` to make
the operation conditional: it proceeds only if the resource still matches, otherwise it returns
`412 Precondition Failed`. This prevents a "lost update" when two clients edit concurrently.

```bash
etag=$(curl -sD - -o /dev/null http://localhost:5268/publishers/acme | awk '/etag/{print $2}' | tr -d '\r')
curl -X PATCH http://localhost:5268/publishers/acme -H "If-Match: $etag" \
  -H 'Content-Type: application/json' -d '{"display_name":"New name"}'   # 200, or 412 if changed
```

The check is **atomic** at the storage layer (a conditional write — `… WHERE update_time = ?`
on SQLite/Postgres, a `ConditionExpression` on DynamoDB), so it holds even under a race. `If-Match: *`
matches any current version (i.e. "must exist"). `If-None-Match` is not supported and returns `400`.

### Reading across collections (AEP-159)

Substitute `-` for a parent collection id in a List request to read across **all** parents:

```bash
curl 'http://localhost:5268/publishers/-/books'                 # every book, regardless of publisher
curl 'http://localhost:5268/publishers/-/books?filter=author%3D%3D%22Orwell%22'  # filter still applies
```

Each returned item keeps its real, full resource name (`publishers/acme/books/1984`), and filtering,
ordering, pagination, and `skip` all work across the widened set. Wildcards compose for deeper
hierarchies (`/publishers/-/books/-/chapters`). On SQLite/Postgres this just drops the parent
equality from the `WHERE`; on DynamoDB a wildcard can't form the index partition key, so it falls
back to a `Scan` + `FilterExpression` (reads more — see [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md)).

## Defining resources

Edit [`src/Aep.Server/resources.yaml`](src/Aep.Server/resources.yaml) (aepc format):

```yaml
name: "bookstore.example.com"
resources:
  publisher:
    singular: "publisher"
    plural: "publishers"
    schema:
      type: object
      properties:
        display_name: { type: string }
  book:
    singular: "book"
    plural: "books"
    parents: ["publisher"]
    schema:
      type: object
      required: ["title"]
      properties:
        title:  { type: string }
        price:  { type: integer }
        tags:   { type: array, items: { type: string } }
```

### Field types and behaviors

Each property takes a `type` (`string`, `integer`, `number`, `boolean`, `array`, `object`),
an optional OpenAPI `format` hint (e.g. `int32`, `int64`, `date-time`), and AEP-203
**field behaviors**. Values are type-checked on write: `format: int32` enforces the 32-bit
range, and `array` elements are validated against their `items` schema (type and enum),
e.g. `tags: ["a", 42]` is rejected.

| Key | Meaning |
|-----|---------|
| `required` (schema-level list) | Must be supplied on Create/Apply (not enforced on PATCH). |
| `read_only: true` | Output-only — server-managed; rejected if a client sends it, returned in responses. |
| `immutable: true` | Settable on Create; a PATCH that includes it is rejected `400 INVALID_ARGUMENT`. |
| `input_only: true` | Accepted on writes but **never returned** in responses (e.g. secrets). |
| `enum: [A, B]` | Constrains a string to the listed values (rejected otherwise), surfaced as an OpenAPI enum. |

```yaml
properties:
  isbn:   { type: string, immutable: true }                 # set once at create
  state:  { type: string, enum: ["DRAFT", "PUBLISHED"] }    # constrained
  api_key:{ type: string, input_only: true }                # write-only
```

Behaviors are reflected in `/openapi.json` as native `readOnly`/`writeOnly` plus an
`x-aep-field-behavior` array (`OUTPUT_ONLY` / `IMMUTABLE` / `INPUT_ONLY`), which aepcli and
ui.aep.dev understand. The standard fields `id`, `uid`, `path`, `create_time`, `update_time`
are always output-only. `uid` (AEP-148) is a server-assigned unique identifier that stays
stable for the resource's lifetime — unlike `id`, which can be reused after a delete.

### Routing-only parents (`not_implemented`)

Sometimes a parent collection is owned by another system and you only want to serve its
**children**. Mark it `not_implemented: true`: its own standard methods answer `501`, but it
still acts as a path segment so descendants are served normally.

```yaml
resources:
  tenant:                     # owned elsewhere — not served here
    singular: "tenant"
    plural: "tenants"
    not_implemented: true
  widget:
    singular: "widget"
    plural: "widgets"
    parents: ["tenant"]       # /tenants/{tenant_id}/widgets works fully
```

`GET/POST /tenants` and `…/tenants/{id}` return `501 Not Implemented`, while
`/tenants/{tenant_id}/widgets/...` serves the full CRUD surface. The parent id is **opaque** —
the server doesn't verify the tenant exists (it isn't stored here), so a widget can be created
under any tenant id. The not-implemented parent is omitted from `/openapi.json` (no schema,
no paths); children still reference it by name in their resource patterns.

## Configuration

Settings come from `appsettings.json` and environment variables (`__` is the nesting separator):

| Setting | Env var | Default | Meaning |
|---------|---------|---------|---------|
| `Resources:File` | `Resources__File` | `resources.yaml` | Path to the service definition |
| `Storage:Provider` | `Storage__Provider` | `sqlite`¹ | Active backend (`sqlite` \| `inmemory` \| `postgres` \| `dynamodb`) |
| `Storage:Sqlite:ConnectionString` | `Storage__Sqlite__ConnectionString` | `Data Source=aep.db` | SQLite connection string |
| `Storage:Postgres:ConnectionString` | `Storage__Postgres__ConnectionString` | `Host=localhost;Database=aep;…` | Npgsql connection string |
| `Storage:DynamoDb:ServiceUrl` | `Storage__DynamoDb__ServiceUrl` | _(real AWS)_ | DynamoDB endpoint; set for a local emulator |
| `Storage:DynamoDb:Region` | `Storage__DynamoDb__Region` | `us-east-1` | AWS region |
| `PageToken:Key` | `PageToken__Key` | _(random per process)_ | Base64 32-byte key for encrypting page tokens; set for stable/multi-instance tokens |

¹ Defaults to `inmemory` when the build excludes SQLite (see below).

### Per-provider tuning

Each backend has a strongly-typed options block under `Storage:<Provider>`, **validated at startup**
(a bad value fails fast with a clear message). The discrete keys layer on top of the connection
string, so set only what you need.

**SQLite** (`Storage:Sqlite:`)

| Key | Default | Meaning |
|-----|---------|---------|
| `JournalMode` | `WAL` | `PRAGMA journal_mode` (WAL, DELETE, TRUNCATE, PERSIST, MEMORY, OFF) |
| `BusyTimeoutMs` | `5000` | `PRAGMA busy_timeout` (lock wait) |
| `Pooling` | _(driver default)_ | Override connection pooling |

**PostgreSQL** (`Storage:Postgres:`) — layered onto the connection string via Npgsql

| Key | Meaning |
|-----|---------|
| `MaxPoolSize` / `MinPoolSize` | Connection-pool bounds |
| `CommandTimeoutSeconds` | Per-command timeout |
| `SslMode` | `Disable` \| `Allow` \| `Prefer` \| `Require` \| `VerifyCA` \| `VerifyFull` |
| `SearchPath` | Schema search path |
| `ApplicationName` | Reported to the server (shows in `pg_stat_activity`) |

**DynamoDB** (`Storage:DynamoDb:`)

| Key | Default | Meaning |
|-----|---------|---------|
| `CredentialsSource` | `Static` | `Static` (AccessKey/SecretKey) \| `Ambient` (env/role — **use on AWS**) \| `Profile` |
| `Profile` | — | Named profile when `CredentialsSource=Profile` |
| `TablePrefix` | _(none)_ | Prefix for every table name |
| `BillingMode` | `PayPerRequest` | `PayPerRequest` \| `Provisioned` |
| `ReadCapacityUnits` / `WriteCapacityUnits` | `5` | Used under `Provisioned` |
| `MaxErrorRetry` / `RetryMode` | _(SDK default)_ | Retry attempts; `Legacy` \| `Standard` \| `Adaptive` |

Via the CLI these are reachable with `--set` (e.g. `--set Storage:Postgres:SslMode=Require`),
or as env vars (`Storage__Postgres__SslMode=Require`).

## Storage backends

Every backend is **opt-in at build time**, so you only ship the dependencies you use:

| Provider | `Storage:Provider` | Build flag | Dependencies | Use for |
|----------|--------------------|-----------|--------------|---------|
| SQLite (default) | `sqlite` | `IncludeSqlite` (on) | `Microsoft.Data.Sqlite` + native `SQLitePCLRaw` | Persistent local/file storage |
| In-memory | `inmemory` | _(always)_ | **none** (pure managed) | Tests, local dev, ephemeral runs |
| PostgreSQL | `postgres` | `IncludePostgres` (off) | `Npgsql` | Shared/relational persistence |
| DynamoDB | `dynamodb` | `IncludeDynamoDb` (off) | `AWSSDK.DynamoDBv2` | Serverless / AWS-native |

The sample host bundles SQLite + in-memory by default. Toggle the others at build time:

```bash
dotnet run --project src/Aep.Server -p:IncludePostgres=true   # adds the postgres option
dotnet build -p:IncludeSqlite=false                            # drop SQLite (publish ≈ 4 DLLs)
```

Selecting a provider the build didn't include fails fast with a clear "unavailable in
this build" error. The **CLI tool bundles all four**, so `aep serve --storage postgres`
works without flags. The same CEL filter is pushed down to each backend server-side — a
SQL `WHERE` for SQLite/Postgres, a DynamoDB `FilterExpression` for DynamoDB.

### Indexes

Speed up filtering/ordering by declaring indexes in code (not in `resources.yaml`):

```csharp
builder.Services.AddAepServer(cfg).AddAepPostgresStore(cfg);
builder.Services.AddResourceIndex("book", "author");          // single field
builder.Services.AddResourceIndex("book", "author", "price"); // composite
```

Each backend uses the declaration appropriately:

- **SQLite / Postgres** create a btree index per declaration (single or composite); the planner uses it for `WHERE`/`ORDER BY`.
- **DynamoDB** creates a GSI per single-field index, keyed on `{parent}+value` with `id` as the sort key. A filter that leads with an **equality** on an indexed field (e.g. `author == "Orwell"`) then becomes a true index `Query` (reading only matching items, still parent-scoped and id-ordered) instead of scanning the partition; the remaining predicates run as a `FilterExpression`. Composite declarations are SQLite/Postgres-only.

### Local Postgres / DynamoDB

```bash
# Postgres
docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=aep postgres:16-alpine
aep serve resources.yaml --storage postgres \
  --connection "Host=localhost;Database=aep;Username=postgres;Password=postgres"

# DynamoDB via floci (a local AWS emulator)
docker run -d -p 4566:4566 hectorvent/floci:latest
aep serve resources.yaml --storage dynamodb --connection http://localhost:4566
```

## Docker

```bash
docker build -t dotnet-aep-server .
docker run -p 8080:8080 -v aepdata:/data dotnet-aep-server
curl http://localhost:8080/openapi.json
```

### How the image is built

The [`Dockerfile`](Dockerfile) is a two-stage build:

1. **build** (`dotnet/sdk:10.0`) — copies `Directory.Build.props` (which sets the target
   framework for every project) and `src/`, then `dotnet restore` + `dotnet publish` the
   `Aep.Server` host to `/app`. `resources.yaml` is `CopyToOutputDirectory`, so it lands in
   `/app` — **baked into the image** at build time (edit it and rebuild to change resources).
2. **runtime** (`dotnet/aspnet:10.0`) — copies `/app`, creates the `/data` directory owned by the
   image's non-root user, and sets the runtime defaults. Entry point is `dotnet Aep.Server.dll`.

### Runtime contract

| | |
|---|---|
| **Port** | `8080` (`EXPOSE 8080`, `ASPNETCORE_HTTP_PORTS=8080`) |
| **Volume** | `/data` — SQLite's database lives at `/data/aep.db`; mount it (`-v aepdata:/data`) to persist |
| **Default storage** | `Storage__Provider=sqlite`, `Storage__Sqlite__ConnectionString=Data Source=/data/aep.db` |
| **Config** | any setting via env (`Storage__Provider`, `Storage__Postgres__*`, `PageToken__Key`, …) — see [Configuration](#configuration) |

Override config at run time, e.g. point at Postgres (in an image built with it — see below):

```bash
docker run -p 8080:8080 \
  -e Storage__Provider=postgres \
  -e Storage__Postgres__ConnectionString="Host=db;Database=aep;Username=postgres;Password=postgres" \
  dotnet-aep-server
```

### Backend-specific (slimmer) images

The storage `Include*` flags are **Docker build args**, so you ship only the backend you deploy.
By default the image bundles SQLite + in-memory; build a different mix with `--build-arg`:

```bash
# Postgres-only image (drops SQLite + its native binding → smaller)
docker build --build-arg IncludeSqlite=false --build-arg IncludePostgres=true -t aep-postgres .

# DynamoDB-only image (ideal for AWS)
docker build --build-arg IncludeSqlite=false --build-arg IncludeDynamoDb=true -t aep-dynamo .
```

Selecting a provider the image wasn't built with fails fast (`unavailable in this build`).
Dropping SQLite removes the native `SQLitePCLRaw` binding, so a Postgres-only image is ~30 MB
smaller than the default (≈ 264 MB vs ≈ 295 MB here). A backend-specific image is the basis for a
slim Lambda image — see the upcoming [AWS example](docs/issues/09-aws-serverless-example.md).

> **Lambda:** because the host is a standard ASP.NET Core app listening on a port, it runs behind
> the [Lambda Web Adapter](https://github.com/awslabs/aws-lambda-web-adapter) with no application
> changes — typically a DynamoDB-only image with the adapter layer added. See
> [`examples/aws-serverless/`](examples/aws-serverless/) for a complete API Gateway + Lambda +
> DynamoDB example, runnable locally against [FLOCI](https://github.com/hectorvent/floci) or
> deployable to AWS with SAM.

## Adding a storage provider

Storage is a single interface, [`IResourceStore`](src/Aep.Storage.Abstractions/Storage/IResourceStore.cs).
To add Postgres or DynamoDB:

1. Create a project (e.g. `src/Aep.Storage.MyDb`) referencing `Aep.Storage.Abstractions`.
2. Implement `IResourceStore` and an [`IStorageProvider`](src/Aep.Storage.Abstractions/Storage/IStorageProvider.cs)
   whose `Register(...)` wires up the store, plus an `AddAepMyDbStore(...)` DI extension. The
   [SQLite](src/Aep.Storage.Sqlite), [Postgres](src/Aep.Storage.Postgres), and
   [DynamoDB](src/Aep.Storage.DynamoDb) providers are worked examples (relational and non-relational).
3. Reference the package/project and call its `AddAep…Store(...)` after `AddAepServer(...)`.
4. For the bundled host/CLI, add an `Include*` build flag + a `case` to their storage switch.

Relational stores can translate the filter to SQL; others can reuse the in-process
[`FilterEvaluator`](src/Aep.Storage.Abstractions/Filtering/FilterEvaluator.cs) (as the
in-memory and DynamoDB stores do).

## Project layout

```
src/
  Aep.Storage.Abstractions/   # model, IResourceStore, IStorageProvider, CEL filter parser + evaluator
  Aep.Storage.Sqlite/         # SQLite store + provider (native binding)
  Aep.Storage.InMemory/       # zero-dependency in-memory store + provider
  Aep.Storage.Postgres/       # PostgreSQL store + provider (Npgsql)
  Aep.Storage.DynamoDb/       # DynamoDB store + provider (AWS SDK)
  Aep.AspNetCore/             # embeddable server library: AddAepServer / MapAepServerAsync, controller, routing, OpenAPI
  Aep.Server/                 # thin runnable host + Dockerfile (sample; not packaged)
  Aep.Cli/                    # dotnet tool (`aep serve`)
tests/
  Aep.Storage.TestKit/        # shared cross-backend conformance suite (one set, run against every store)
  Aep.Storage.Sqlite.Tests/   # conformance + SQLite-specific (index DDL) + CEL filter + options
  Aep.Storage.InMemory.Tests/ # conformance suite against the in-memory store
  Aep.Storage.Postgres.Tests/ # conformance + Postgres-specific (Testcontainers)
  Aep.Storage.DynamoDb.Tests/ # conformance + DynamoDB-specific GSI tests (floci via Testcontainers)
  Aep.Storage.Benchmarks/     # BenchmarkDotNet storage benchmarks (not run by `dotnet test`)
  Aep.Server.Tests/           # WebApplicationFactory integration tests
```

Packable projects (`AepServer.*`) opt in via `<IsPackable>true</IsPackable>`; shared
NuGet metadata lives in [`Directory.Build.props`](Directory.Build.props). Run
`dotnet pack -c Release` to build all seven packages.

## Tests

```bash
dotnet test
```

The Postgres and DynamoDB store tests use [Testcontainers](https://dotnet.testcontainers.org/),
so they need a running **Docker** daemon — they spin up `postgres:16-alpine` and
`hectorvent/floci` (the DynamoDB emulator) automatically. The rest run with no external services.

### Cross-backend conformance suite

The store contract — round-trip, duplicate, partial update, delete, list (scope, pagination,
filter), wildcard reads (AEP-159), optimistic concurrency (AEP-154), and `uid` — is defined **once**
in [`tests/Aep.Storage.TestKit`](tests/Aep.Storage.TestKit) and run against **every** backend, so
all four are proven to behave identically and new behaviors are added in one place (not four). Each
backend's test project subclasses it and adds only its backend-specific tests (index DDL, GSI
planning, options).

### Performance benchmarks

[`tests/Aep.Storage.Benchmarks`](tests/Aep.Storage.Benchmarks) holds BenchmarkDotNet benchmarks for
read/list/filter throughput (with vs without a declared index) and the page-token codec. They run
against the Docker-free backends and aren't part of `dotnet test` — see that project's
[README](tests/Aep.Storage.Benchmarks/README.md).

### AEP conformance check

On top of the unit/integration tests, [`tests/conformance/`](tests/conformance/) runs two
external, AEP-aware checks — wired into CI ([`.github/workflows/conformance.yml`](.github/workflows/conformance.yml)):

```bash
tests/conformance/run.sh        # OpenAPI lint + aepcli end-to-end, one command
```

- **OpenAPI lint** — the official [aep-openapi-linter](https://github.com/aep-dev/aep-openapi-linter)
  (a pinned Spectral ruleset) checks `/openapi.json` for AEP conformance; the gate fails on any
  error or warning that isn't a documented, triaged deviation in
  [`tests/conformance/.spectral.yaml`](tests/conformance/.spectral.yaml).
- **aepcli end-to-end** — drives the running server through the full CRUD lifecycle and asserts
  on outcomes. It probes the aepcli binary and fails fast if it's too old (see the version note below).

The generated spec passes the **full** AEP ruleset with no suppressions; see
[`tests/conformance/README.md`](tests/conformance/README.md) for details and
[`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) for intentional, ruleset-permitted quirks.

### Driving it with aepcli by hand

Because the server publishes an AEP OpenAPI spec, [aepcli](https://github.com/aep-dev/aepcli)
can drive it directly:

```bash
go install github.com/aep-dev/aepcli/cmd/aepcli@latest   # needs a recent build (see note)
dotnet run --project src/Aep.Server &

API=http://localhost:5268/openapi.json
aepcli --server-url http://localhost:5268 $API publisher create acme --display_name "Acme"
aepcli --server-url http://localhost:5268 $API book create 1984 --publisher acme --title "1984" --author "Orwell" --price 1200 --published
aepcli --server-url http://localhost:5268 $API book get 1984 --publisher acme
aepcli --server-url http://localhost:5268 $API book list --publisher acme
aepcli --server-url http://localhost:5268 $API book delete 1984 --publisher acme
```

Notes:
- aepcli derives every resource and its typed flags from `/openapi.json`, validating the spec end-to-end.
- Use an aepcli built against a recent `aep-lib-go` (Feb 2026+). Older releases (e.g. the
  current `@latest` tag) have a parser bug that drops `get`/`update`/`delete` for item paths —
  the spec itself is correct (verified against current `aep-lib-go`).
- aepcli applies the resource's `required` fields to *all* write verbs, so `book update` asks
  for `--title` even though PATCH is a partial update server-side. Pass it (or use `--@data`).
  This is expected — see [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md).
- `delete` returns `204 No Content`; aepcli prints a cosmetic "unexpected end of JSON input"
  while trying to format the empty body — the deletion still succeeds.
