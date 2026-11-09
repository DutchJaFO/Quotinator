# A reset sets aside a truncated database, not only an unrecognisable one

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #482, #348, #327

## Preconditions

**Beyond the profile.** A real, fully seeded database is cut to half its length while the container is
stopped: a genuine SQLite file with a valid header and missing pages, which is what an interrupted
write or a filled disk actually leaves behind.

**Why this is its own document rather than a second case inside
[`01`](01-a-reset-sets-aside-a-database-it-cannot-read.md)** (developer decision, 2026-08-27): a
truncated database and a file that is not a database are different faults reaching SQLite through
different code paths, and each has a variable its own `Determinism` has to pin. Folding them together
would let one pass on the other's behalf. It matters more since #482: a truncated file's first page
still reads, so only a check that reads every page tells it apart from a healthy one.

## Determinism

- **Half the file, not a fixed byte count.** The bundled dataset's size moves between releases; a
  literal length would silently stop truncating anything once it grew past it.
- **The WAL sidecars are deleted.** A surviving `-wal` can carry enough recent pages for SQLite to open
  the database anyway, which would make the outcome depend on how recently the seed checkpointed.
- **The container is removed before the file is touched.** `destroy` is `docker rm -f`, a kill rather
  than a clean stop, so the sidecars can survive it, which is why the bullet above deletes them rather
  than relying on a checkpoint.
- **A copy of the truncated file is kept outside the data directory**, so step 5 can recognise the
  set-aside file by its content; its name carries a timestamp no step can predict.
- **The second start waits for *listening*, then polls `status`**, since degrading is the expected outcome
  and the startup wait page answers `503` too.
- **Nothing here reads what the database holds.** The fault is the file being cut short, so step 1
  asserts only that a healthy database exists, never a count, which would depend on the bundled sources.

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-02 --delete C:\repos\Quotinator\.claude\temp\qt-backup-02-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-02 --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-02-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-02
dotnet script scripts/testing/http.csx -- --url http://localhost:18382/api/v1/health --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-02-out\health-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-02-out\health-seeded.json --equals status=healthy
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-file C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db
```

**Expected:** every command exits `0`: the application reports `healthy`, and `quotinatordata.db` is a file.

**On failure:** a fresh seed that cannot reach healthy is not this test's subject. Stop.

### 2. Stop the container and cut the database in half

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind C:\repos\Quotinator\.claude\temp\qt-backup-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --truncate-half C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db --delete C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db-shm --copy C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db C:\repos\Quotinator\.claude\temp\qt-backup-02-out\original.db
```

**Expected:** the `file` command exits `0`, reporting the new length as half the old one, both megabytes
rather than bytes.

**On failure:** a file-in-use error means the container did not stop. Re-run `destroy` and retry.

### 3. Start the current build and confirm it degrades

```
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-02 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18382/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-02-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-02-out\health.json --equals status=unhealthy
```

**Expected:** the `json` command exits `0` on `status = unhealthy`.

**On failure:** `healthy` means the truncation did not take. Re-run from step 2.

### 4. Reset, and confirm the application recovers

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18382/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18382/api/v1/health --expect 200 --status
```

**Expected:** `200` from the Reset, and `200` from `/health` after it.

**On failure:** a `409` with obstacle `SourceUnreadable` is the refusal #482 removed. A `200` followed by
an unhealthy `/health` means the Reset ran without replacing the file.

### 5. Confirm the file was set aside, and no backup was made of it

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-02\backups\unreadable "*.db" 1 --expect-identical-in C:\repos\Quotinator\.claude\temp\qt-backup-02-out\original.db C:\repos\Quotinator\.claude\temp\qt-backup-02\backups\unreadable --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-02\backups "*.db" 0
```

**Expected:** the command exits `0`: exactly one file in `backups/unreadable/`, byte for byte the truncated
file, and no file in `backups/` itself.

**This is the case a weaker check would get wrong.** The truncated file's header and first page are
intact, so a Reset that only opened the file would judge it readable and copy it into `backups/` as a
backup. Restoring that would put the damaged database back.

**On failure:** a file in `backups/` itself means a damaged file was kept as a backup.

### 6. Read the notification that says so

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18382/api/v1/notifications --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-02-out\notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-02-out\notifications.json --where items:metadataKind=unreadabledatabasesetaside --length-equals items=1 --contains items.0.body=unreadable
```

**Expected:** exactly one notification of that kind, whose text names the `unreadable` folder.

### 7. Reset the now-healthy database, the positive control

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18382/api/v1/admin/database/reset --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-02\backups "*.db" 1 --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-02\backups\unreadable "*.db" 1
dotnet script scripts/testing/http.csx -- --url http://localhost:18382/api/v1/health --expect 200 --status
```

**Expected:** the Reset answers `200`, one backup is now in `backups/`, the reserved folder still holds only
the file step 5 found, and `/health` stays `200`.

**The positive control.** A readable database is backed up and never set aside, so steps 4 to 6 are about
the truncation and nothing else.

## Observed effect

**Measured 2026-11-09** against `quotinator:local` built from the working tree at `271c4c54`, the database
truncated from 4,714,496 to 2,357,248 bytes.

The start degrades to `unhealthy`, and the Reset answers `200` with `/health` back at `200`. The truncated
file is moved to `backups/unreadable/quotinatordata_unreadable_20261109T085041559Z.db`, byte-identical to
the truncated original, with no backup of it in `backups/`. The notification reads exactly as in
[`01`](01-a-reset-sets-aside-a-database-it-cannot-read.md), naming this file: a truncated file is told
apart from a readable one by the same check, not by a second path.

The control Reset of the rebuilt database took its own backup, `quotinatordata_v9_20261109T085046518Z.db`,
and left the set-aside file where it was.

## Canary

Build `69800a23`, the commit before #482's first commit, as `quotinator:canary-482`, shared with `backup/01`,
and run steps 1 to 4. Steps 1 to 3 are the control and read the same on both builds: the file is half its
original length and the start degrades. Step 4 must answer `409` with obstacle `SourceUnreadable`.

**Run 2026-11-09** against `69800a23`: steps 1 to 3 passed, and step 4 answered `409` with
`backupObstacle=SourceUnreadable`.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind C:\repos\Quotinator\.claude\temp\qt-backup-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-02 --delete C:\repos\Quotinator\.claude\temp\qt-backup-02-out
```

**Expected:** the container and both directories are gone.
