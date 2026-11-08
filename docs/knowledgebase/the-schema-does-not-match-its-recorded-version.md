# Startup reports that the database's schema does not match the one its recorded version defines

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #305

## Symptom

`/api/v1/health` answers `503` with a reason that starts:

```
The database's schema does not match the one its recorded version defines:
```

followed by every missing or different object, for example `missing table System_Notification` or
`column System_Notification.DateDeleted has declared type 'TEXT' where 'INTEGER' is expected`, and the
two remedies. The log carries the same reason at `Critical` with the `[Server]` prefix, and one
`[Database - Init] schema integrity:` line per difference at `Error`.

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`. The pages,
the API reference at `/scalar/v1`, the spec at `/openapi/v1.json` and the admin endpoints still answer.

## Cause

At startup, after its migrations, Quotinator compares the database's schema with the schema its recorded
version defines, in every aspect it measures: tables, columns, foreign keys, indexes, views and triggers.
Something was missing or different. The recorded version says the schema is current, but the schema on
disk is not what that version produces, so a query could fail at any point. Common causes:

- **A table or column was removed or changed outside Quotinator**, by hand, by a tool, or by restoring
  part of a database.
- **An upgrade was interrupted** partway, leaving the version recorded but the schema not.

An *extra* object, one no migration defines, is reported in the log at `Warning` but does not cause this.

## Remedy

`GET /api/v1/admin/database/integrity` lists every difference for both databases, without changing
anything. Then either:

- **Run a database Reset** (`POST /api/v1/admin/database/reset`), which rebuilds the schema at this
  build's own version and discards what the database holds now. Export the audit trail first
  (`GET /api/v1/admin/audit/export`) if you want to keep it, and reseed afterwards to load the bundled
  content again.
- **Restore a backup** taken before the schema changed, and restart. Backups are kept in the `backups`
  folder of the data directory, and are listed by `GET /api/v1/admin/backups`.

## Notes

The check runs again after a Reset. A Reset whose rebuilt schema still does not match answers `500` with
the same kind of reason and leaves the application degraded; that would mean the build itself is at
fault, and is worth an issue.

The changelog database is checked the same way. A difference there is logged at `Error` with the
`[Changelog - Init]` prefix and does not degrade the application: the changelog is served from its JSON
files instead, and it is regenerated on the next start.
