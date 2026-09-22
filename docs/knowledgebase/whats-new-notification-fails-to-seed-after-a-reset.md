# The log reports that the what's-new notification could not be seeded, after a reset

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#419](https://github.com/DutchJaFO/Quotinator/issues/419)

## Symptom

One `Error` line repeated as the exception is rethrown — ten times when measured — each naming the same
exception id:

```
[Runtime - Exception] 07487885 thrown: SqliteException
Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 19: 'FOREIGN KEY constraint failed'.
```

followed by one `Warning`:

```
[Server] Failed to seed the #81 what's-new notification — non-fatal, startup continues.
```

It appears only when a database reset was run within about a second of the application becoming
healthy.

## Does it prevent the app or API from functioning?

**No.** Startup continues and every endpoint serves normally. The one consequence is that the what's-new
notification is missing for that boot — the next restart writes it.

## Cause

The what's-new notification is written on a background task that starts during startup, using the
application-version row that existed when the task began. A reset rebuilds the database, removing that
row, so the write that follows it has nothing to point at and the database refuses it.

The window is roughly the first second after the application reports healthy. A reset run at any other
time cannot produce this.

## Remedy

Nothing is required — the notification returns on the next restart, and no data is affected. To avoid
it, leave a second or two between the application reporting healthy and running a reset.

## Notes

Found 2026-09-22 while running the automated-test documents for
[#411](https://github.com/DutchJaFO/Quotinator/issues/411). Reproduced 5 times out of 5 by firing the
reset the instant health answered, and 0 times out of 5 with a one-second wait before it.
