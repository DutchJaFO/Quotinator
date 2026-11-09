# A reset of an unreadable database refuses when the data directory cannot be written

**Smoke:** no
**Environment:** Constrained
**Deterministic:** yes
**Traces to:** #482

## Preconditions

**Beyond the profile.** The quote database is replaced with bytes that are not a SQLite database, and the
data directory is then mounted read-only (`--read-only-data`). A Reset of an unreadable file needs no
backup, but it does need to move the file aside and write a new one in its place, and a read-only data
directory allows neither.

## Determinism

- **`backups/` exists before the read-only run**, created by step 1, so the refusal is about the data
  directory and not about a backup folder that was never made.
- **`--read-only-data`, not `--read-only`.** The root filesystem being unwritable is
  [`startup-and-degradation/04`](../startup-and-degradation/04-migration-replay-under-restricted-write.md)'s
  subject.
- **The file is replaced while the container is stopped, and its sidecars deleted**, for the reasons
  [`01`](01-a-reset-sets-aside-a-database-it-cannot-read.md) gives.
- **The read-only start waits for *listening*, then polls `status`**, since degrading is the expected
  outcome and the startup wait page answers `503` too.

## Steps

### 1. Seed a healthy database, creating `backups/`

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-09 --delete C:\repos\Quotinator\.claude\temp\qt-backup-09-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-09\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-09-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-09 --port 18389 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-directory C:\repos\Quotinator\.claude\temp\qt-backup-09\backups --expect-file C:\repos\Quotinator\.claude\temp\qt-backup-09\quotinatordata.db
```

**Expected:** the environment reports healthy, and the `file` command exits `0`.

**On failure:** a fresh seed that cannot reach healthy is not this test's subject. Stop.

### 2. Stop the container and replace the database with non-database bytes

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-09 --bind C:\repos\Quotinator\.claude\temp\qt-backup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --write-text C:\repos\Quotinator\.claude\temp\qt-backup-09\quotinatordata.db "this file is not a SQLite database" --delete C:\repos\Quotinator\.claude\temp\qt-backup-09\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-09\quotinatordata.db-shm --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-09\quotinatordata.db 34
```

**Expected:** the `file` command exits `0`, reporting the file at 34 bytes.

### 3. Start with the data directory read-only, and confirm it degrades

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-09 --port 18389 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-09 --read-only-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18389/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-09-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-09-out\health.json --equals status=unhealthy
```

**Expected:** the `json` command exits `0` on `status = unhealthy`.

### 4. Attempt a reset, and read what it says

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18389/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-09-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-09-out\reset.json --equals resetObstacle=DataDirectoryNotWritable --not-empty remedies --absent backupObstacle
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-09\quotinatordata.db 34 --expect-absent C:\repos\Quotinator\.claude\temp\qt-backup-09\backups\unreadable
```

**Expected:** `409`, naming the reset obstacle `DataDirectoryNotWritable` with at least one remedy and no
backup obstacle; the unreadable file is still in place at 34 bytes, and nothing was set aside.

**The obstacle's name is the point.** Before #482 this same call also answered `409`, but blamed the
backup folder (`DestinationFileNotWritable`). No backup is needed for a file that cannot be read; what stops the Reset is
that the data directory cannot be written, and a refusal that names the wrong thing sends the operator
to fix the wrong thing.

**On failure:** a `backupObstacle` in the body means the refusal is still framed as a backup problem. A
changed file length or an `unreadable` folder means the Reset moved or wrote something before refusing.

### 5. Remount writable, and confirm a reset then works

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-09 --port 18389 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-09 --wait-listening
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18389/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18389/api/v1/health --expect 200 --status
```

**Expected:** the Reset answers `200`, and `/health` `200` after it.

**The positive control, and the proof the remedy works.** Step 4 asserts a refusal, which on its own
would pass against a build that refused every reset. Restoring write access is the remedy the refusal
names, and this shows it resolves the condition.

**On failure:** a `409` here means the refusal is not specific to the read-only mount.

## Observed effect

**Not yet measured** against a build carrying #482; see *Observed effects awaiting measurement* in the
suite's README.

## Canary

`quotinator:canary-482`, built from `69800a23`, the commit before #482, shared with `01`, and run through
steps 1 to 4. Steps 1 to 3 read the same on both builds. Step 4 answers `409` there too, but with a
`backupObstacle` and no `resetObstacle`, so its `json` check must fail.

**Run 2026-11-09** against `69800a23`: step 4 answered `409` with `backupObstacle=DestinationFileNotWritable`,
blaming the backup folder for a Reset that needs no backup, and the `json` check failed on both the missing
`resetObstacle` and the present `backupObstacle`. The file stayed at 34 bytes and nothing was set aside.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-09 --bind C:\repos\Quotinator\.claude\temp\qt-backup-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-09 --delete C:\repos\Quotinator\.claude\temp\qt-backup-09-out
```

**Expected:** the container and both directories are gone.
