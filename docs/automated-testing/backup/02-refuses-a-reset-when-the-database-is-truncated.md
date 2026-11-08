# A reset refuses when the database is truncated, not only when it is unrecognisable

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #348, #327

## Preconditions

**Beyond the profile.** A real, fully seeded database is cut to half its length while the container is
stopped: a genuine SQLite file with a valid header and missing pages, which is what an interrupted
write or a filled disk actually leaves behind.

**Why this is its own document rather than a second case inside
[`01`](01-refuses-a-reset-when-the-source-cannot-be-read.md)** (developer decision, 2026-08-27): a
truncated database and a file that is not a database are different faults reaching SQLite through
different code paths, and each has a variable its own `Determinism` has to pin. Folding them together
would let one pass on the other's behalf.

## Determinism

- **Half the file, not a fixed byte count.** The bundled dataset's size moves between releases; a
  literal length would silently stop truncating anything once it grew past it.
- **The WAL sidecars are deleted.** A surviving `-wal` can carry enough recent pages for SQLite to open
  the database anyway, which would make the outcome depend on how recently the seed checkpointed.
- **The container is removed before the file is touched.** `destroy` is `docker rm -f`, a kill rather
  than a clean stop, so the sidecars can survive it, which is why the bullet above deletes them rather
  than relying on a checkpoint.
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
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --truncate-half C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db --delete C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db-shm
```

**Expected:** the `file` command exits `0`, reporting the new length as half the old one, both megabytes
rather than bytes.

**On failure:** a file-in-use error means the container did not stop. Re-run `destroy` and retry.

### 3. Start the current build and attempt a reset

```
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-02 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18382/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-02-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-02-out\health.json --equals status=unhealthy
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18382/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-02-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-02-out\reset.json --equals backupObstacle=SourceUnreadable --length-equals remedies=2
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-backup-02-out\reset.json "remedies" --expect-no-text C:\repos\Quotinator\.claude\temp\qt-backup-02-out\reset.json "allowNoBackup"
```

**Expected:** `status = unhealthy`, then `409` with obstacle `SourceUnreadable`, two remedies, and none
of them mentioning `allowNoBackup`.

A truncated file reports the same obstacle as an unreadable one, and that is correct rather than a
missed distinction: SQLite cannot read either, so neither can be backed up, and the remedy is the same.
What this document proves is that the *route* to that outcome works for truncation too, which the
unit tests, which use non-database bytes, do not exercise.

**On failure:** `healthy` means the truncation did not take. A `500` from the reset means the refusal is
not reached on this path even though it is on `01`'s.

### 4. Apply the remedy, and confirm a reset then works

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind C:\repos\Quotinator\.claude\temp\qt-backup-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-02\quotinatordata.db
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-02
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18382/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18382/api/v1/health --expect 200 --status
```

**Expected:** healthy, the reset `200`, and `/health` still `200` after it.

**The positive control.** Step 3 asserts a refusal; on its own that would pass against a build refusing
every reset. This shows the refusal is caused by the truncation and nothing else, and that the remedy
the refusal names actually resolves it.

**On failure:** a `409` means the refusal is not specific to the sabotage, and step 3 proves nothing.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `ea9bd594`: a 4,669,440-byte database cut to
2,334,720 bytes degrades at startup (`status = unhealthy`) and answers a reset attempt with `409` /
`SourceUnreadable`, carrying the same two remedies as `01`, neither naming the override: move or delete
the file and restart, or restore an older backup. With the file deleted, the next start reached healthy
and a reset answered `200`, after which `/health` still read `200`. The PowerShell form this document had
until then read the same.

Before #348 this same call returned an unhandled `500`.

## Canary

Build `4cfe1006`, the commit before step 8 of
[#348](https://github.com/DutchJaFO/Quotinator/issues/348)'s plan, as `quotinator:canary348`, and run
steps 2 and 3 against it. The image is shared with `backup/01`, `06` and `07`.

Step 3 must fail to report `409` with `obstacle=SourceUnreadable`. A truncated file is still a valid
SQLite header, which is the distinction in this document's title: the unrecognisable case was handled
before the half-size one was, so this build either answers `500` or accepts the reset.

**Which of those two it does is the finding, and it decides what this document is worth.** A `500`
means the refusal was missing; a `200` means a truncated database was wiped behind a success. Record
the answer rather than treating either as the expected red.

Step 2 is the control and reads the same on both builds: the file is half its original length and
still megabytes, so the setup reached its premise regardless of how the reset then behaved.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind C:\repos\Quotinator\.claude\temp\qt-backup-02
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-02 --delete C:\repos\Quotinator\.claude\temp\qt-backup-02-out
```

**Expected:** the container and both directories are gone.
