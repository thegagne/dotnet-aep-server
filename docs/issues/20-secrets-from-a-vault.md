# 20 — Load secrets from a vault

**Theme:** Security · **Status:** proposed

## Summary

Source sensitive configuration — `PageToken:Key`, DB connection strings/passwords, OAuth signing
material — from a secrets manager rather than plain env/appsettings, so secrets aren't baked into
images or sitting in process env.

## Scope

- Pluggable secret resolution via the standard `IConfiguration` provider model: AWS Secrets Manager
  / SSM Parameter Store, Azure Key Vault, HashiCorp Vault, or file-mounted secrets.
- Reference secrets by name in config (e.g. `Storage:Postgres:ConnectionString` resolved from a
  secret ref), so the rest of the app is unchanged.
- Document the pattern per deployment (the AWS example uses Secrets Manager / the task role).
- Never log resolved secret values; redact in any config dump.

## Acceptance criteria

- [ ] At least one vault provider wired (AWS Secrets Manager, given the serverless example) and documented.
- [ ] `PageToken:Key` and DB credentials can be sourced from the vault without code changes.
- [ ] Secrets are never logged; resolution failures fail fast at startup.

## References

- `IConfiguration` providers; relates to [#01](01-configurable-database-connections.md),
  [#09](09-aws-serverless-example.md), [#16](16-oauth-client-credentials-validator.md)
- README "Pagination" (`PageToken:Key`)
