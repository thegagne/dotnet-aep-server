# 26 — Soft delete + undelete (AEP-164)

**Theme:** API surface (AEP) · **Status:** proposed

## Summary

Support soft deletion: `Delete` marks a resource deleted (and stops returning it by default) rather
than removing it, with an `Undelete` method to restore it, per AEP-164.

## Scope

- Opt-in per resource (e.g. `methods.delete.soft: true` / an `undelete` method in `resources.yaml`).
- Soft delete sets a server-managed `delete_time` (and the resource becomes excluded from Get/List
  by default); hard delete remains available when not opted in.
- **Undelete** — `POST .../{id}:undelete` restores it (clears `delete_time`).
- **List** gains `show_deleted` to include soft-deleted resources; Get of a soft-deleted resource
  returns it marked deleted (or 404 unless `show_deleted`), per AEP-164.
- Expiry/purge of soft-deleted rows after a retention window (optional).
- All four backends store `delete_time` (reuse the field-behavior + migration patterns from
  [#13](13-system-assigned-uid.md)).

## Acceptance criteria

- [ ] Soft `Delete` sets `delete_time` and hides the resource by default; `:undelete` restores it.
- [ ] `List?show_deleted=true` includes soft-deleted resources; default excludes them.
- [ ] Behavior is consistent across backends and reflected in OpenAPI.

## References

- AEP-164 (undelete / soft delete); relates to [#13](13-system-assigned-uid.md) (standard fields + migration)
