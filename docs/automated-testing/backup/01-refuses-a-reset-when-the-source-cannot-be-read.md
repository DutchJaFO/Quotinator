# A reset refuses when the database cannot be read, instead of returning 500

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #348, #327

## Preconditions

**Beyond the profile.** The database file is replaced with bytes that are not a SQLite database, while
the container is stopped. The changelog database and the `keys/` directory are left alone, since the fault
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
- **The second start waits for *listening*, then polls `status`.** Degrading is the expected outcome, and
  the startup wait page answers `503` with `status=starting` too, so only the field tells the two apart.
- **Nothing here reads what the database holds.** The fault is the file itself, so step 1 asserts only that
  a healthy database exists, never a count, which would depend on the bundled sources.

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-01 --delete C:\repos\Quotinator\.claude\temp\qt-backup-01-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-01 --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-01-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-01 --port 18381 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet script scripts/testing/http.csx -- --url http://localhost:18381/api/v1/health --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-01-out\health-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-01-out\health-seeded.json --equals status=healthy
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db
```

**Expected:** every command exits `0`: the application reports `healthy`, and `quotinatordata.db` is a file.

**On failure:** if a fresh seed cannot reach healthy, nothing below is about backups. Stop and fix that.

### 2. Stop the container and replace the database with non-database bytes

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --write-text C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db "this file is not a SQLite database" --delete C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db-shm --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db 34
```

**Expected:** the `file` command exits `0`, reporting the file at 34 bytes.

**On failure:** a file still megabytes in size means the write did not land, and every step below would
be asserting against a healthy database, which proves nothing. Stop.

### 3. Start the current build and confirm it degrades

```
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-01 --port 18381 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-01 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18381/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-01-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-01-out\health.json --equals status=unhealthy
```

**Expected:** the `json` command exits `0` on `status = unhealthy`.

**On failure:** `healthy` means the file was not actually replaced. Re-run from step 2.

### 4. Attempt a reset, and read what it says

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18381/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-01-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-01-out\reset.json --equals backupObstacle=SourceUnreadable --length-equals remedies=2
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-backup-01-out\reset.json "remedies" --expect-no-text C:\repos\Quotinator\.claude\temp\qt-backup-01-out\reset.json "allowNoBackup"
```

**Expected:** `409`, and both TestProbe commands exit `0`: the obstacle is `SourceUnreadable`, there are
two remedies, and none of them mentions `allowNoBackup`.

**This is the defect #348 exists to remove.** Before it, this exact call returned an unhandled `500`,
while `/health` was telling the operator to make it. The missing override is the second half: a database
SQLite will not open cannot be dropped table by table either, so offering the override would name a remedy
that cannot work. The `remedies` text the same read finds is what makes its absence a reading rather than
a read of an empty file.

**On failure:** a `500` means the refusal is not reached at all. A `409` that offers the override means
the guidance has regressed to promising something measured not to work.

### 5. Confirm the override cannot rescue it either

```
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18381/api/v1/admin/database/reset?allowNoBackup=true" --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-01-out\reset-override.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-01-out\reset-override.json --equals backupObstacle=SourceUnreadable
```

**Expected:** `409`, the same refusal and obstacle.

**On failure:** a `200` would mean the reset ran against a file SQLite cannot open, which is not
possible; investigate what actually happened rather than accepting the pass.

### 6. Apply the remedy this document names, and confirm a reset then works

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-01 --port 18381 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18381/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18381/api/v1/health --expect 200 --status
```

**Expected:** the container reaches healthy, the reset returns `200`, and `/health` still answers `200`
after it.

**Two things at once, and both are load-bearing.** It is this document's **positive control**: every step
above asserts a refusal, so without a passing case the whole document would still pass against a build
that refused *everything*. And it is the proof that the remedy step 4 hands the operator, *"move or
delete the database file, and restart"*, actually resolves the condition, rather than being advice
nobody checked.

**On failure:** a `409` here means the refusal is not specific to the sabotage, and every assertion
above is worthless. A container that will not reach healthy means the named remedy does not work, which
is a defect in the guidance rather than in this test.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `ea9bd594`, both in the PowerShell form this
document had until then and in this one, with the same readings.

`/health` reports `unhealthy` with the generic initialisation-failure reason, which names a database Reset
as its remedy. That reset answers `409`, with or without `allowNoBackup=true`:

> `409 Conflict`, *"Reset refused: no backup could be taken"*
> `backupObstacle: SourceUnreadable`
> *"The database file itself cannot be read: it is corrupt, truncated, or not a database. No backup of
> it is possible by any means."*
> Remedies: move or delete the database file and restart; or restore an older backup in its place.

Two remedies, neither naming the override. With the file deleted, the next start reached healthy and a
reset answered `200`, after which `/health` still read `200`. The operator is told the truth: this file
cannot be backed up, so it cannot be reset in place either, and the way out is to replace it from outside
the application.

## Canary

Build `4cfe1006`, the commit before step 8 of
[#348](https://github.com/DutchJaFO/Quotinator/issues/348)'s plan, as `quotinator:canary348`, and run
steps 2 to 5 against it. `backup/06` and `backup/07` already use that build, so the image is shared
rather than built per document.

Step 4 must answer `500` rather than `409`. That is the defect in this document's own title: the
reset reached a database it could not read and failed raw, with no obstacle named and no remedy
offered, so `obstacle`, `remedyCount` and the Knowledgebase address have nothing to carry them.

Steps 2 and 3 are expected to behave the same on both builds, and they are the control. The file is
34 bytes either way and the start degrades either way, so step 4's answer is about how the reset
reports rather than about whether the database was actually broken.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-01 --delete C:\repos\Quotinator\.claude\temp\qt-backup-01-out
```

**Expected:** the container and both directories are gone.
