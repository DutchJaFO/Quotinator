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
- **The audit trail is read after the Reset, and searched rather than indexed.** A Reset replaces the
  whole database, the audit table included (ADR 014), so the `Database` rows afterwards are the Reset's
  own and the one recording the skipped backup. Both are written in the same second, and rows written in
  the same second have no defined order (#486), so the step looks for the row by its content, not its
  position.

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-10 --delete C:\repos\Quotinator\.claude\temp\qt-backup-10-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-10\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-10-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-10 --port 18390 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-10
```

**Expected:** the environment reports healthy.

### 2. Fill the backups folder to its ceiling

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-10\backups\filler.db 1073741824 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-10\backups\filler.db 1073741824
```

**Expected:** the `file` command exits `0`, reporting the filler at `1073741824` bytes.

### 3. Attempt a reset without the override, and confirm it refuses

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18390/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\reset.json --equals backupObstacle=BudgetExceeded --contains "remedies=allowNoBackup"
```

**Expected:** `409`, and the `json` command exits `0`: the obstacle is `BudgetExceeded`, and one remedy
names `allowNoBackup`.

**On failure:** a `200` means the folder does not refuse a backup, and step 4 proves nothing about the
override.

### 4. Take the offered override, and confirm the reset completes

```
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18390/api/v1/admin/database/reset?allowNoBackup=true" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18390/api/v1/health --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-10\backups "*.db" 1
```

**Expected:** the Reset answers `200`, `/health` `200` after it, and `backups/` still holds one `.db`
file, the filler: no backup was written.

**This is the assertion the document exists for.** Before #348's step 29 the override passed the
readiness check, then the Reset took its own backup regardless, hit the same full folder, and answered
the same `409` the override was meant to get past.

**On failure:** a `409` with `backupObstacle=BudgetExceeded` is that defect. A second `.db` file means a
backup was taken from somewhere the ceiling does not reach.

### 5. Read the skipped backup in the audit trail

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18390/api/v1/admin/audit?table=Database" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-10-out\audit.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-10-out\audit.json --contains items=BackupSkipped --contains items=Reset
```

**Expected:** the `json` command exits `0`: among the `Database` rows, one records the skipped backup,
beside the Reset's own.

**On failure:** no row means the Reset ran without a backup and nothing outlasting the log says so,
which is what #348 requirement 5 forbids.

## Observed effect

**Measured 2026-11-12** against `quotinator:local` built from the working tree of #348's step 29.

With the folder at its ceiling, the Reset without the override answers `409`, *Reset refused: no backup
could be taken*, `backupObstacle=BudgetExceeded`, with three remedies: remove old backups, raise the
ceiling, or retry with `allowNoBackup=true`. Retried with it, the Reset answers `200` and `/health` stays
`200`. `backups/` still holds only the filler. The audit trail then holds two `Database` rows written in
the same second: `Reset` and `BackupSkipped`.

## Canary

Build the commit before #348's step 29 as `quotinator:canary-348-29` and run steps 1 to 4. Steps 1 to 3
read the same on both builds. Step 4's Reset must answer `409` with `backupObstacle=BudgetExceeded`, so
its `http.csx` command fails on the expected `200`.

**Run 2026-11-12** against `bd84d987`: steps 1 to 3 green, the refusal offering `allowNoBackup`. Step 4's
Reset with the override answered `409` (*Expected 200, got 409*), the defect this document exists for;
`backups/` still held only the filler.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-10 --bind C:\repos\Quotinator\.claude\temp\qt-backup-10
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-10 --delete C:\repos\Quotinator\.claude\temp\qt-backup-10-out
```

**Expected:** the container and both directories are gone.
