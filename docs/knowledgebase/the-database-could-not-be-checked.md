# Startup reports that the database could not be checked

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #490

## Symptom

`/api/v1/health` answers `503`:

```
{"status":"unhealthy","reason":"The database could not be checked. It holds changes that were not yet written into the main file, which happens after an unclean stop, and checking them safely needs a working copy in the data directory that could not be made. Free disk space in the data directory, or restore write access to it, and restart: the database is then checked again. See https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/the-database-could-not-be-checked.md"}
```

The log carries a Warning naming the layer that could not be checked and why:

```
[Database - Check] main: the Pages layer could not be checked: The -wal holds data, so the database can only be checked through a copy, and none could be made in …
```

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`. The pages,
the API reference at `/scalar/v1` and the spec at `/openapi/v1.json` still answer.

## Cause

Startup checks the database before anything writes to it ([ADR 028](../architecture-decisions/028-a-database-is-checked-in-layers-cheapest-first.md)).
After an unclean stop, such as a power cut, a container killed before it finished stopping, or a crash,
the file beside the database, `quotinatordata.db-wal`, still holds changes that were committed but not yet
written into the main file. Those changes are part of the database.

Nothing in SQLite's documentation says whether the database can be read together with that file without
changing it, so it is never checked in place. The database and its `-wal` are copied into the
`.database-check` folder inside the data directory, and the copy is checked. Here the copy could not be
made:

- **The data volume is full,** or has less free space than the database and its `-wal` together. The
  common cause.
- **The data directory cannot be written,** so the `.database-check` folder cannot be created in it.

A check that could not run is never treated as a pass, so startup stops rather than serving from a
database nobody has checked.

## Remedy

- **Free space on the data volume,** at least the size of `quotinatordata.db` and `quotinatordata.db-wal`
  together, then restart. Old backups in the backup folder are a common place to free it, listed by
  `GET /api/v1/admin/backups`.
- **Restore write access to the data directory,** then restart.

**Do not delete `quotinatordata.db-wal`.** It holds committed changes, and deleting it loses them.

A Reset also works, if starting from an empty database is acceptable: it cannot show the file is a sound
copy, so it sets the file aside in the `unreadable` folder inside the backup folder rather than backing it
up. Moving the file needs no extra space.

## Notes

Decided in #490 as decision D. The check of a database without changes waiting in its `-wal`, which is
what a clean stop leaves, reads the file in place and needs no copy and no free space.
