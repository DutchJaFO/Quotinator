# Startup reports that database initialisation failed on a file that cannot be read

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #457, #482, #490, #348, #444

## Symptom

`/api/v1/health` answers `503`:

```
{"status":"unhealthy","reason":"Database initialisation failed. This often means the database's recorded schema version doesn't match its actual on-disk schema (e.g. after an interrupted upgrade). Resolve with an explicit database Reset (POST /api/v1/admin/database/reset) or by stopping the app, deleting the database file, and restarting."}
```

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`. The pages,
the API reference at `/scalar/v1` and the spec at `/openapi/v1.json` still answer.

## Cause

The same reason is given for three different conditions:

- **The database file cannot be read at all.** The file is not a SQLite database, or it was cut short,
  for example by a disk filling up, a copy interrupted partway, or something other than Quotinator
  writing to it. Despite what the reason says, the schema version is not the problem: nothing in the file
  can be read.
- **The file reads, but is damaged inside.** A page is damaged, or an index no longer agrees with its
  table. Since #490 the database check finds this at startup, before the database is opened; with
  `database_startup_check` set to `fast`, a damaged index is not checked at startup and does not give
  this reason.
- **The schema does not match its recorded version**, for instance after an interrupted upgrade. This is
  what the reason describes.

`GET /api/v1/admin/database/integrity` tells them apart: its `layers` name the layer that failed, with
the engine's own finding. It answers while the application is degraded.

## Remedy

Run the Reset the reason names, `POST /api/v1/admin/database/reset`, or press *Reset the database* on the
page the application shows while degraded. It works in every case. The Reset checks the old file at full
depth first, whatever `database_startup_check` says, and a file is a sound copy only when its format,
journal, pages and indexes all pass:

- **A file that is not a sound copy** is never backed up. It is moved, with any `quotinatordata.db-wal` and
  `quotinatordata.db-shm` beside it, into the `unreadable` folder inside the backup folder, a new database
  takes its place, and a notification names the file and says why it was set aside: the check layer that
  failed, with its status and the engine's finding, or, when the check passed but the backup copy could not
  read the file, the backup's own error. It counts towards backup storage until it is removed.
- **A sound copy** is backed up first, then replaced.

The new database is empty: run a reseed to load the bundled content. To get your data back instead,
**restore a backup** taken before the file was damaged; backups are listed by
`GET /api/v1/admin/backups`.

If the Reset answers `409` with a `resetObstacle`, the file could not be moved: the data directory or the
backup folder cannot be written, or the move to a backup folder on another volume ran out of space. The
response's `remedies` say what to change; nothing was moved.

## Notes

Since #305, a schema that does not match its recorded version is found before this reason can be given,
and reported with its own: see [the schema does not match the one its recorded version
defines](the-schema-does-not-match-its-recorded-version.md). This reason remains for what that check
cannot reach, a file that cannot be read among them.

Found by #327's `startup-and-degradation/08` and `09` (2026-10-23); the damaged index by #490's `15`
(2026-11-15). #457 tracks the reason telling an
unreadable file apart from a schema-version mismatch. #482 made the Reset rebuild a file it cannot read,
instead of refusing. [#481](https://github.com/DutchJaFO/Quotinator/issues/481) plans listing,
downloading and removing a set-aside file, and
[#444](https://github.com/DutchJaFO/Quotinator/issues/444) restoring a backup from inside the
application, local or uploaded.
