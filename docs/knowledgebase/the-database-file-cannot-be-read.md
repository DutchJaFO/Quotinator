# Startup reports that the database file is damaged or cannot be opened

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #457, #482, #490, #348, #444

## Symptom

`/api/v1/health` answers `503` with `status` `unhealthy`, and a reason that begins with what is wrong with
the database file. For a file that is not a database at all:

```
{"status":"unhealthy","reason":"The database file is not a database this application can open: it is not a database at all, or its header is damaged. The application does not use it. Reset the database with POST /api/v1/admin/database/reset: the database file is set aside in the \"unreadable\" folder inside the backups folder, and a new, empty database replaces it. Restore an older backup in place of the database file, then restart. Or stop the application and restart it after deleting the database file, which creates a new, empty database in its place. See https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/the-database-file-cannot-be-read.md"}
```

The reason's first sentence names the fault, one per layer of the database check:

| The reason begins | What the check found |
|---|---|
| *The database file exists but could not be opened* | The file is there, but opening it failed |
| *The database file is not a database this application can open* | The file's header is not a database's |
| *The write-ahead file beside the database … is damaged or does not belong to it* | The `quotinatordata.db-wal` file is invalid |
| *Parts of the database file are damaged or missing* | Pages cannot be read |
| *The database file opens, but an index no longer agrees with its table* | An index or a unique constraint does not hold |

The same reason is given when the application is already running and
`GET /api/v1/admin/database/integrity` finds one of these.

Earlier builds gave every one of these a generic reason saying the recorded schema version probably did not
match the on-disk schema. That diagnosis was wrong for all of them.

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`. The pages,
the API reference at `/scalar/v1` and the spec at `/openapi/v1.json` still answer.

## Cause

- **The file could not be opened.** Its permissions do not allow the application to read it, or another
  process holds it locked. The file itself may be intact.
- **The file is not a database**, or its header is damaged: something other than Quotinator wrote over
  it, or a different file was put in its place.
- **The write-ahead file is damaged** or belongs to a different database. It holds changes not yet written
  into the main file, so they cannot be trusted.
- **Pages are damaged or missing.** The file was cut short or overwritten, for example by a disk filling
  up, a copy interrupted partway, or something other than Quotinator writing to it.
- **An index disagrees with its table**, or a unique constraint does not hold. The file opens, but what it
  returns cannot be trusted. With `database_startup_check` set to `fast`, this is not checked at startup.

`GET /api/v1/admin/database/integrity` names the layer that failed, with the engine's own finding. It
answers while the application is degraded.

## Remedy

**For a file that could not be opened**, restore read access to the file, or stop whatever holds it
open, then restart. Nothing is wrong with the file itself in this case, so try this first.

**For a damaged file**, in the order the reason gives them:

1. **Reset the database**, `POST /api/v1/admin/database/reset`, or press *Reset the database* on the page
   the application shows while degraded. The Reset checks the old file at full depth first, whatever
   `database_startup_check` says, and a file is a sound copy only when its format, journal, pages and
   indexes all pass. A file that is not a sound copy is never backed up: it is moved, with any
   `quotinatordata.db-wal` and `quotinatordata.db-shm` beside it, into the `unreadable` folder inside the
   backups folder, a new database takes its place, and a notification names the file and says why it was
   set aside. It counts towards backup storage until it is removed. The new database is empty: run a
   reseed to load the bundled content.
2. **Restore a backup** taken before the file was damaged, in place of the file, then restart. Backups are
   listed by `GET /api/v1/admin/backups`.
3. **Stop the application, delete the database file, and restart.** A new, empty database is created.

For a file that could not be opened, a Reset also sets it aside and replaces it, which is why the reason
offers it last there: the file may be intact.

**When the reason does not offer a Reset**, it says why: the `unreadable` folder inside the backups folder
could not be created or written, so a Reset would be refused. Restore write access to the backups folder,
and remove anything occupying the `unreadable` name inside it; a Reset then works. If a Reset answers
`409` with a `resetObstacle` anyway, its `remedies` say what to change; nothing was moved.

## Notes

A schema that does not match its recorded version is reported with its own reason: see [the schema does
not match the one its recorded version defines](the-schema-does-not-match-its-recorded-version.md). An
unwritable data directory is reported with #326's reason, which names write access, since a Reset cannot
work there either.

The generic reason, *"Database initialisation failed for a cause this build does not recognise"*,
remains for what no layer of the check foresaw. The application log names the error.

Found by #327's `startup-and-degradation/08` and `09` (2026-10-23); the damaged index by #490's `15`
(2026-11-15). #457 gave each fault its own reason. #482 made the Reset rebuild a file it cannot read,
instead of refusing. [#481](https://github.com/DutchJaFO/Quotinator/issues/481) plans listing,
downloading and removing a set-aside file, and
[#444](https://github.com/DutchJaFO/Quotinator/issues/444) restoring a backup from inside the
application, local or uploaded.
