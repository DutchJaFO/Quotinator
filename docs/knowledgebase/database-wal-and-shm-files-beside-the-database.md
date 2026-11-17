# Files named quotinatordata.db-wal and quotinatordata.db-shm appear beside the database

**Kind:** question
**Entry code:** —
**Affected versions:** all
**GitHub issue:** #490, #326

## Symptom

The data directory holds two files besides `quotinatordata.db`:

```
quotinatordata.db
quotinatordata.db-shm
quotinatordata.db-wal
```

Sometimes they are there and sometimes they are not. While Quotinator runs, the `-wal` can be several
megabytes.

## Does it prevent the app or API from functioning?

**No.** These are SQLite's own working files. They are expected, and they need no action while the
application runs.

## Cause

This is how the SQLite engine works, not something Quotinator adds. Quotinator opens its database in
SQLite's write-ahead logging (WAL) mode, and in that mode SQLite keeps two files beside the database
([sqlite.org/wal.html](https://www.sqlite.org/wal.html)):

- **`-wal`, the write-ahead log.** Changes are written here first and copied into the main file later,
  at a *checkpoint*. Until then, the latest committed changes exist only in the `-wal`. SQLite calls it
  "part of the persistent state of the database".
- **`-shm`, the shared-memory index.** It helps connections find their way through the `-wal`. It holds
  nothing that is not also in the other two files.

**When they disappear**, in SQLite's words: "When the last connection to a database closes, that
connection does one last checkpoint and then deletes the WAL and its associated shared-memory file." So
after a clean stop of Quotinator, only `quotinatordata.db` is left, and it is complete on its own.

**When they stay behind:**

- **After an unclean stop.** If the process "exits without cleanly shutting down the database
  connection", SQLite says the `-wal` "might be retained on disk". This is what a crash, a power cut, or
  `docker kill` / `docker rm -f` produces. On the next open, SQLite runs a recovery and applies what the
  `-wal` holds. Nothing needs doing.
- **After a tool opens the file read-only.** Measured, not documented by SQLite: Quotinator's own
  DbInspector, opening the database read-only after a clean stop, left an empty `-wal` and a `-shm`
  behind (2026-11-17). An empty `-wal` holds no changes.

## Remedy

Nothing, while the application runs. When you handle the database file yourself:

- **To copy or move the database, stop Quotinator cleanly first** (stop the add-on, or `docker stop`,
  never `docker kill` or `docker rm -f`). Then `quotinatordata.db` alone is the whole database.
- **If a `-wal` is still there, copy it together with the database**, and keep the two names matching.
  SQLite warns that a database separated from its `-wal` can lose committed transactions or be
  corrupted.
- **Never delete a `-wal` by hand.** SQLite's only safe way to remove it is to open the database and
  close it again, which is what starting and cleanly stopping Quotinator does.
- **Backups Quotinator takes need none of this.** Each one, in the `backups` folder, is a single complete
  file.

## Notes

Found while running #490's `backup/07` (2026-11-17), where a test copied the database without its `-wal`.
[#326](https://github.com/DutchJaFO/Quotinator/issues/326) measured that whether these files are present
also decides whether a database on a read-only volume can be read at all: SQLite can read a WAL database
without write access only if the `-wal` and `-shm` already exist, or if the folder lets SQLite create them.
