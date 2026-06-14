# 25 — Container image hardening

**Theme:** Security / Ops · **Status:** proposed

## Summary

Harden the published image: smaller, rootless, scanned, and provenance-tracked, so production
containers carry minimal attack surface.

## Scope

- **Minimal base** — evaluate the chiseled/distroless ASP.NET runtime image (`...-noble-chiseled`)
  vs. the current `aspnet:10.0`; smaller surface, no shell/package manager.
- **Non-root** — confirm/enforce the non-root user (`$APP_UID` is already used for `/data`); read-only
  root filesystem where feasible.
- **Vulnerability scanning** in CI (Trivy/Grype) failing on high/critical CVEs.
- **SBOM** generation + image **signing/provenance** (e.g. Syft + cosign / SLSA attestations).
- Pin base image by digest; document the slim backend-specific build recipe (already in [#08](08-document-container-build.md)).

## Acceptance criteria

- [ ] Image runs as non-root on a minimal base; size reduced vs. today.
- [ ] CI scans the image and fails on high/critical CVEs.
- [ ] An SBOM is produced and the image is signed / attested.

## References

- `Dockerfile`; relates to [#08](08-document-container-build.md), [#24](24-ci-for-the-test-suite.md),
  [#09](09-aws-serverless-example.md)
