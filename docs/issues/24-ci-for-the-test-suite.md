# 24 — CI for the .NET test suite

**Theme:** Quality / Ops · **Status:** proposed

## Summary

Run the full .NET test suite in CI on every PR. Today only the AEP-conformance workflow
(`.github/workflows/conformance.yml`) runs; the 178-test unit/integration/cross-backend suite isn't
gated, so a regression can merge.

## Scope

- A GitHub Actions workflow: `dotnet build` + `dotnet test` across all test projects on push/PR.
- Provide **Docker** in the runner so the Testcontainers-backed Postgres/DynamoDB suites run (they
  spin `postgres:16-alpine` and `hectorvent/floci`).
- Matrix or single-job; fail the PR on any test failure; surface results.
- Optionally collect coverage; optionally run the benchmark project in `--job dry` as a smoke check.
- Keep it fast (cache NuGet, parallelize where safe).

## Acceptance criteria

- [ ] `dotnet test` (all projects, incl. Testcontainers suites) runs on every PR and gates merge.
- [ ] The workflow has Docker available for Postgres/DynamoDB tests.
- [ ] Failures are clearly reported; runs are reasonably fast (caching).

## References

- `.github/workflows/conformance.yml` (existing); `tests/` (all projects)
