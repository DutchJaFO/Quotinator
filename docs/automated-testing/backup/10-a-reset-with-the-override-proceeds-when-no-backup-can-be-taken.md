# A reset with the override proceeds when no backup can be taken

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #348

## Preconditions

A normal seeded run on a writable bind mount, with the backups folder filled to its ceiling by a real file
inside `backups/`, so no backup can be taken. The database itself is healthy and readable: this is the
override's own case, an operator who has been told why no backup is possible and chooses to go on without
one.

## Determinism

- **The ceiling, not the quota**, for the reason [`05`](05-a-full-quota-is-resolvable-from-inside-the-application.md)
  gives: a filler past only the 90% operating quota refuses nothing, since a backup there is taken from
  the reserve.
- **One sparse filler file of `1073741824` bytes**, the whole of the default 1 GB `MaxBackupStorageGb`.
- **The same call twice, differing only in `allowNoBackup=true`.** Step 3 is the control for step 4: it
  proves the folder really refuses a backup, so step 4's `200` is the override's doing and not an empty
  folder's.
- **The proof is the data and the responses, never the audit trail.** That the Reset ran is proven by
  the quotes: present before, still present after the refusal, gone after the override, read from
  `/quotes` and from the Reset's own response. That no backup was taken is proven by the files in
  `backups/`. An audit row only says the application *believes* something happened, so it proves
  neither.
- **The audit trail is read in step 5 for #348 requirement 5 only**, which asks for the skipped backup
  to be recorded where it outlasts the log: that the record exists is the requirement itself, not
  evidence for anything else. A Reset replaces the whole database, the audit table included (ADR 014),
  and writes its own `Reset` row in the same second as the `BackupSkipped` row. Rows written in the same
  second have no defined order (#486), so the step looks for the row by its content, not its position.

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-10 --delete C:\repos\Quotinator\.claude\temp\qt-backup-10-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-10\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-10-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-10 --port 18390 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-10
dotnet script scripts/testing/http.csx -- --url http://localhost:18390/api/v1/quotes --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\quotes-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\quotes-before.json --greater-than totalCount=0
```

**Expected:** the environment reports healthy, and `/quotes` holds the bundled quotes: there is something
for a Reset to remove.

### 2. Fill the backups folder to its ceiling

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-10\backups\filler.db 1073741824 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-10\backups\filler.db 1073741824
```

**Expected:** the `file` command exits `0`, reporting the filler at `1073741824` bytes.

### 3. Attempt a reset without the override, and confirm it refuses

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18390/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\reset.json --equals backupObstacle=BudgetExceeded --contains "remedies=allowNoBackup"
dotnet script scripts/testing/http.csx -- --url http://localhost:18390/api/v1/quotes --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\quotes-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\quotes-refused.json --greater-than totalCount=0
```

**Expected:** `409`, and both `json` commands exit `0`: the obstacle is `BudgetExceeded`, one remedy
names `allowNoBackup`, and the quotes are still there, so the refusal changed nothing.

**On failure:** a `200` means the folder does not refuse a backup, and step 4 proves nothing about the
override. A `409` with the quotes gone means the Reset ran anyway after refusing.

### 4. Take the offered override, and confirm the reset completes

```
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18390/api/v1/admin/database/reset?allowNoBackup=true" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\reset-override.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\reset-override.json --equals quotes=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18390/api/v1/quotes --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\quotes-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\quotes-after.json --equals totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-10\backups "*.db" 1
```

**Expected:** the Reset answers `200` with `quotes=0`, `/quotes` then reads `totalCount=0`, and
`backups/` still holds one `.db` file, the filler: the database was replaced, and no backup was written.

**This is the assertion the document exists for.** Before #348's step 29 the override passed the
readiness check, then the Reset took its own backup regardless, hit the same full folder, and answered
the same `409` the override was meant to get past.

**On failure:** a `409` with `backupObstacle=BudgetExceeded` is that defect. A `200` with quotes still in
`/quotes` means the Reset reported success without replacing the database. A second `.db` file means a
backup was taken from somewhere the ceiling does not reach.

### 5. Confirm the skipped backup is recorded (#348 requirement 5)

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18390/api/v1/admin/audit?table=Database" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\audit.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\audit.json --contains items=BackupSkipped --contains items=Reset
```

**Expected:** the `json` command exits `0`: among the `Database` rows, one records the skipped backup,
beside the Reset's own.

This step proves nothing about the Reset or the backup; steps 3 and 4 did that from the data. It checks
only that the record requirement 5 asks for is written.

**On failure:** no row means the Reset ran without a backup and nothing outlasting the log says so,
which is what #348 requirement 5 forbids.

## Observed effect

**Measured 2026-11-12** against `quotinator:local` built from the working tree of #348's step 29.

With the folder at its ceiling, the Reset without the override answers `409`, *Reset refused: no backup
could be taken*, `backupObstacle=BudgetExceeded`, with three remedies: remove old backups, raise the
ceiling, or retry with `allowNoBackup=true`. Retried with it, the Reset answers `200` and `/health` stays
`200`. `backups/` still holds only the filler. The audit trail then holds two `Database` rows written in
the same second: `Reset` and `BackupSkipped`.

**Re-measured 2026-11-12** after the proof moved from the audit trail to the data, against an image built
from `ec03fed5`. `/quotes` read `totalCount=795` after the seed and still `795` after the refused Reset.
The override's Reset answered `200` with every count `0` (`quotes=0`, `sources=0`, through
`conversations=0`, `reports=[]`), and `/quotes` then read `totalCount=0`. `backups/` held only the
filler.

## Canary

Build the commit before #348's step 29 as `quotinator:canary-348-29` and run steps 1 to 4. Steps 1 to 3
read the same on both builds. Step 4's Reset must answer `409` with `backupObstacle=BudgetExceeded`, so
its `http.csx` command fails on the expected `200`, and `/quotes` still holds the seeded quotes.

**Run 2026-11-12** against `bd84d987`: steps 1 to 3 green, the refusal offering `allowNoBackup`. Step 4's
Reset with the override answered `409` (*Expected 200, got 409*), the defect this document exists for;
`backups/` still held only the filler.

**Re-run 2026-11-12** against `bd84d987` with the data checks: steps 1 to 3 green, `/quotes` at `795`
before and after the refusal. Step 4's Reset with the override answered `409` again, and `/quotes` still
read `totalCount=795` after it: the database was never replaced.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-10 --bind C:\repos\Quotinator\.claude\temp\qt-backup-10
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-10 --delete C:\repos\Quotinator\.claude\temp\qt-backup-10-out
```

**Expected:** the container and both directories are gone.
