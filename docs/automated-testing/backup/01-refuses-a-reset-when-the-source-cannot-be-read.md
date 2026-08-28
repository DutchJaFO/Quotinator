# A reset refuses when the database cannot be read, instead of returning 500

**Smoke:** no
**Environment:** Fresh
**Traces to:** #348, #327

## Preconditions

**Beyond the profile.** The database file is replaced with bytes that are not a SQLite database, while
the container is stopped. The changelog database and the `keys/` directory are left alone — the fault
under test is the quote database specifically, and damaging more would make it ambiguous which one the
refusal is about.

The container binds its data directory to a host path (`--bind`) so the file can be replaced from the
host; a named volume would put it somewhere this document cannot reach.

## Determinism

- **The file is replaced, not truncated.** Truncation is
  [`02`](02-refuses-a-reset-when-the-database-is-truncated.md)'s subject and reaches SQLite through a
  different code path; keeping them apart is what lets each name its own cause.
- **The WAL sidecars are deleted along with it.** A surviving `-wal` alongside a garbage main file
  leaves SQLite with two disagreeing sources, and which one it complains about first is timing, not a
  property of the test.
- **The container is stopped before the file is touched.** Writing under a running container races its
  own connections, and the result would depend on when the write landed.
- **The second start waits for *listening*, not healthy.** Degrading is the expected outcome, so waiting
  for `200` would spend the whole timeout and then fail for the wrong reason.

## Steps

### 1. Seed a healthy database

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-01"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-01 --port 18381 `
  --image quotinator:local --bind $dataDir

(Invoke-RestMethod "http://localhost:18381/api/v1/version").database.quotes
```

**Expected:** a non-zero quote count, and `quotinatordata.db` present in `$dataDir`.

**On failure:** if a fresh seed cannot reach healthy, nothing below is about backups. Stop and fix that.

### 2. Stop the container and replace the database with non-database bytes

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-01"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind $dataDir

Set-Content -Path "$dataDir\quotinatordata.db" -Value "this file is not a SQLite database" -NoNewline
Remove-Item "$dataDir\quotinatordata.db-wal","$dataDir\quotinatordata.db-shm" -ErrorAction SilentlyContinue
(Get-Item "$dataDir\quotinatordata.db").Length
```

**Expected:** the file is 34 bytes.

**On failure:** a file still megabytes in size means the write did not land, and every step below would
be asserting against a healthy database — which proves nothing. Stop.

### 3. Start the current build and confirm it degrades

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-01"
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-01 --port 18381 `
  --image quotinator:local --bind $dataDir --wait-listening

dotnet script scripts/testing/http.csx -- --url "http://localhost:18381/api/v1/health" --expect 503 --status
```

**Expected:** `503`.

**On failure:** a `200` means the file was not actually replaced — re-run from step 2.

### 4. Attempt a reset, and read what it says

```powershell
$r = dotnet script scripts/testing/http.csx -- --url "http://localhost:18381/api/v1/admin/database/reset" `
  --method POST --expect 409 | ConvertFrom-Json
"obstacle=$($r.backupObstacle)"
"remedyCount=$($r.remedies.Count)"
"offersOverride=$([bool]($r.remedies -match 'allowNoBackup'))"
```

**Expected:** `409`, with `obstacle=SourceUnreadable`, `remedyCount` of 2, and
`offersOverride=False`.

**This is the defect #348 exists to remove.** Before it, this exact call returned an unhandled `500` —
while `/health` was telling the operator to make it. `offersOverride=False` is the second half: a
database SQLite will not open cannot be dropped table-by-table either, so offering the override would
name a remedy that cannot work.

**On failure:** a `500` means the refusal is not reached at all. A `409` that offers the override means
the guidance has regressed to promising something measured not to work.

### 5. Confirm the override cannot rescue it either

```powershell
dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18381/api/v1/admin/database/reset?allowNoBackup=true" `
  --method POST --expect 409 --status
```

**Expected:** `409` — the same refusal.

**On failure:** a `200` would mean the reset ran against a file SQLite cannot open, which is not
possible; investigate what actually happened rather than accepting the pass.

### 6. Apply the remedy this document names, and confirm a reset then works

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-01"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind $dataDir

Remove-Item "$dataDir\quotinatordata.db"

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-01 --port 18381 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18381/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
```

**Expected:** the container reaches healthy, and the reset returns `200`.

**Two things at once, and both are load-bearing.** It is this document's **positive control**: every step
above asserts a refusal, so without a passing case the whole document would still pass against a build
that refused *everything*. And it is the proof that the remedy step 4 hands the operator — *"move or
delete the database file, and restart"* — actually resolves the condition, rather than being advice
nobody checked.

**On failure:** a `409` here means the refusal is not specific to the sabotage, and every assertion
above is worthless. A container that will not reach healthy means the named remedy does not work, which
is a defect in the guidance rather than in this test.

## Observed effect

**Measured 2026-08-28** against `quotinator:local`.

`/health` reports `503` with the generic initialisation-failure reason, which names a database Reset as
its remedy. That reset now answers:

> `409 Conflict` — *"Reset refused — no backup could be taken"*
> `backupObstacle: SourceUnreadable`
> *"The database file itself cannot be read — it is corrupt, truncated, or not a database. No backup of
> it is possible by any means."*
> Remedies: move or delete the database file and restart; or restore an older backup in its place.

The operator is told the truth: this file cannot be backed up, so it cannot be reset in place either,
and the way out is to replace it from outside the application.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-01"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
