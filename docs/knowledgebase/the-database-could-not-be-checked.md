# Startup reports that there is not enough free space to check the database

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #490

## Symptom

`/api/v1/health` answers `503`:

```
{"status":"unhealthy","reason":"There is not enough free space in the data directory to check the database. It holds changes that were not yet written into the main file, and checking them safely needs a working copy of the database beside it. Free disk space in the data directory and restart: the database is then checked again. See https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/the-database-could-not-be-checked.md"}
```

`GET /api/v1/admin/database/integrity` reports the main database's `storage` layer as `failed`, with a
finding naming the room the copy needs and the room the volume has.

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`. The pages,
the API reference at `/scalar/v1` and the spec at `/openapi/v1.json` still answer.

## Cause

The database is checked before anything writes to it, and again whenever the integrity endpoint is asked
([ADR 028](../architecture-decisions/028-a-database-is-checked-in-layers-cheapest-first.md)). When the file
beside the database, `quotinatordata.db-wal`, holds changes not yet written into the main file, those changes
are part of the database. That is the case after an unclean stop, such as a power cut or a container killed
before it finished stopping, and often while the application runs.

Nothing in SQLite's documentation says whether the database can be read together with that file without
changing it, so it is never checked in place. The database and its `-wal` are copied into the
`.database-check` folder inside the data directory, and the copy is checked. The room that copy needs, the
database and its `-wal` together, is measured first. Here the volume had less than that, so the check's
Storage layer failed, and startup stops rather than serving from a database nobody has checked.

The same result follows when the copy is attempted and the operating system refuses it for want of space,
because something else took the room after it was measured.

## Remedy

**Free space on the data volume,** at least the size of `quotinatordata.db` and `quotinatordata.db-wal`
together, then restart. Old backups in the backup folder are a common place to free it, listed by
`GET /api/v1/admin/backups`.

**Do not delete `quotinatordata.db-wal`.** It holds committed changes, and deleting it loses them.

A Reset does not get round this on its own. Room for the check's copy is room a backup needs too, so a Reset
refuses with `409`, naming `InsufficientDiskSpace`, as it does for any backup it cannot take. Retrying it with
`allowNoBackup=true` accepts going without a restore point: the Reset then sets the file aside in the
`unreadable` folder inside the backup folder, since nothing showed it sound, which needs no extra room, and
puts an empty database in its place.

## Notes

Decided in #490 (developer, 2026-11-15): a copy that cannot be made is what the check's Storage layer found,
never an inconclusive result. A database with no changes waiting in its `-wal`, which is what a clean stop
leaves, is read in place and needs no copy and no free space. A data directory that cannot be written at all
gives its own reason instead.
