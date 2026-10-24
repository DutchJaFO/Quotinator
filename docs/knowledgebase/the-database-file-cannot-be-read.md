# Startup reports that database initialisation failed, and a Reset is refused

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #457, #348, #444

## Symptom

`/api/v1/health` answers `503`:

```
{"status":"unhealthy","reason":"Database initialisation failed. This often means the database's recorded schema version doesn't match its actual on-disk schema (e.g. after an interrupted upgrade). Resolve with an explicit database Reset (POST /api/v1/admin/database/reset) or by stopping the app, deleting the database file, and restarting."}
```

and the Reset it recommends answers `409`, *"Reset refused: no backup could be taken"*, with obstacle
`SourceUnreadable`. Retrying with `allowNoBackup=true` is refused the same way.

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`. The pages,
the API reference at `/scalar/v1` and the spec at `/openapi/v1.json` still answer.

## Cause

The same reason is given for two different conditions, and the Reset's refusal tells them apart:

- **The database file cannot be read at all** (the Reset refuses with `SourceUnreadable`). The file is
  not a SQLite database, or it was cut short, for example by a disk filling up, a copy interrupted
  partway, or something other than Quotinator writing to it. Despite what the reason says, the schema
  version is not the problem: nothing in the file can be read.
- **The schema does not match its recorded version** (the Reset runs). This is what the reason describes,
  for instance after an interrupted upgrade; a Reset resolves it.

## Remedy

For a file that cannot be read:

- **Restore a backup** taken before the file was damaged, if you want your data back. Backups are kept in
  the `backups` folder of the data directory, and are listed by `GET /api/v1/admin/backups`.
- **Otherwise start with a new database**: stop the container, delete `quotinatordata.db` (and any
  `quotinatordata.db-wal` and `quotinatordata.db-shm` beside it) from the data directory, and start it
  again. Quotinator creates a fresh database and loads the bundled content.

For a schema that does not match its version, run the Reset the reason names.

## Notes

Found by #327's `startup-and-degradation/08` and `09` (2026-10-23). #457 tracks the reason telling an
unreadable file apart from a schema-version mismatch. The `allowNoBackup` override is meant to let a
Reset proceed with no restore point, and refusing here is a gap in it, to be closed under #348.
[#444](https://github.com/DutchJaFO/Quotinator/issues/444) plans restoring a backup from inside the
application, local or uploaded, which will make the first remedy above an in-app action.
