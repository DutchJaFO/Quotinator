# #352 — Restore a stored backup, refusing one taken ahead of this build

**Status:** Planning
**GitHub issue:** #352
**Tiers required:** T1, T2
**Depends on:** none — pairs with [#349](https://github.com/DutchJaFO/Quotinator/issues/349) and [#353](https://github.com/DutchJaFO/Quotinator/issues/353), any order

---

## Description

#348 and #350 each hand the operator "restore an older backup" as a remedy, and neither has a route.
#348's `SourceUnreadable` guidance says *"Restore an older backup in place of the unreadable file, then
restart"* — a filesystem instruction a Home Assistant add-on user cannot act on. #349 makes stored
backups visible, downloadable, creatable and removable; this makes them usable.

---

## Next action

**Write the steps and the verification checklist, then the red tests.**

Every design question is settled, including what happens after a successful restore (2026-08-29 — see
*After a restore, the application reports* below). The steps are deliberately unwritten until the issue
is started: #349 lands first and builds the `{name}` guard, the backup reader and the create endpoint
this issue reads from, so writing them against today's code would mean writing them twice.

---

## Design

Three facts were measured while filing the issue, and each removed a requirement rather than adding one.

**The copy direction is the same API, reversed.** Verified against
[sqlite.org/backup.html](https://www.sqlite.org/backup.html) (2026-08-29): the online backup API copies
one database into another *"replacing any original contents of the target database"*, and its own
`loadOrSaveDb` example runs this direction. In `Microsoft.Data.Sqlite` the source is the connection the
method is called on, so a restore is `backupConnection.BackupDatabase(liveConnection)`.

**A partial restore cannot leave a half-written database.** Per
[sqlite3_backup_finish](https://www.sqlite.org/c3ref/backup_finish.html), an incomplete backup's
write-transaction on the destination is rolled back. This is a guarantee to assert, not a property to
engineer.

**Concurrent writers cannot corrupt a restore.** The same page: the first `backup_step` takes an
exclusive lock on the destination, held until completion. What remains is the opposite case — a restore
*blocked* by in-flight writes, which is an availability condition to report rather than a correctness
hazard.

### Why no pre-restore backup

Rejected as a requirement (developer decision, 2026-08-29). It would bolt a second, independent
data-retention decision onto an endpoint with one job, which `CLAUDE.md`'s endpoint side-effect policy
forbids outright; every restore would deposit a snapshot of the state the operator was deliberately
discarding into the quota #348 refuses on; and the protection it looks like it buys is already given by
the rollback guarantee above. An operator who wants a restore point takes one explicitly, through #349's
create endpoint.

### After a restore, the application reports and the operator decides

A restored file can carry a different schema version and different row counts than the running process
last read, so the instance must not keep serving from state that no longer matches the database. It
degrades with a reason instead, in the shape #326 established, naming what happened and what is required
to finish (developer decision, 2026-08-29).

**It does not act on the operator's behalf.** What the operator is choosing is how and when the restored
database finishes coming up — not whether: one that is behind this build needs its pending migrations
applied before anything can be served from it.

**Only one route to that exists today, and this issue does not add a second.** Nothing in the admin
surface re-runs initialisation — `/database/reset` is the opposite of what is wanted here, since it
would drop the database just restored — so restarting is the single remedy reported, applying pending
migrations through the normal startup path. That is a normal action for a Home Assistant add-on operator
(supervisor UI) and for a standalone Docker user. Whether an in-place "finish initialising" route should
exist is deferred to its own issue, to be filed once this path has actually been used rather than
designed speculatively beside the feature that would need it.

### The one refusal that is this issue's own

A backup taken by a *newer* build is refused before anything is overwritten. Restoring one manufactures
the overshoot state #350 exists to make degrade — the application would serve from a schema whose shape
it does not know. The backwards direction is the normal case and is accepted; migrations replay forward.

---

## Steps

Not yet written — see *Next action*. The design is settled; the steps wait on #349 landing the guard,
the reader and the create endpoint this issue builds on.

---

## Verification checklist

Not yet written — it is written once the steps above are, and before any implementation starts.
