# A reset sets aside a database it cannot read, and rebuilds it

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #482, #348, #327

## Preconditions

**Beyond the profile.** The database file is replaced with bytes that are not a SQLite database, while
the container is stopped. The changelog database and the `keys/` directory are left alone, since the fault
under test is the quote database specifically, and damaging more would make it ambiguous which one the
Reset is acting on.

The container binds its data directory to a host path (`--bind`) so the file can be replaced, and the
backup folder read, from the host; a named volume would put both somewhere this document cannot reach.

## Determinism

- **The file is replaced, not truncated.** Truncation is
  [`02`](02-a-reset-sets-aside-a-truncated-database.md)'s subject and reaches SQLite through a different
  code path; keeping them apart is what lets each name its own cause.
- **The WAL sidecars are deleted along with it.** A surviving `-wal` alongside a garbage main file
  leaves SQLite with two disagreeing sources, and which one it complains about first is timing, not a
  property of the test.
- **The container is stopped before the file is touched.** Writing under a running container races its
  own connections, and the result would depend on when the write landed.
- **A copy of the replacement bytes is kept outside the data directory**, so step 5 can recognise the
  set-aside file by its content. Its name carries a timestamp, which no step can predict.
- **The second start waits for *listening*, then polls `status`.** Degrading is the expected outcome, and
  the startup wait page answers `503` with `status=starting` too, so only the field tells the two apart.
- **Nothing here reads what the database holds before the fault.** The fault is the file itself, so step 1
  asserts only that a healthy database exists, never a count, which would depend on the bundled sources.

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

**On failure:** if a fresh seed cannot reach healthy, nothing below is about a Reset. Stop and fix that.

### 2. Stop the container and replace the database with non-database bytes

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --write-text C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db "this file is not a SQLite database" --write-text C:\repos\Quotinator\.claude\temp\qt-backup-01-out\original.db "this file is not a SQLite database" --delete C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db-shm --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-01\quotinatordata.db 34
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

### 4. Reset, and confirm the application recovers

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18381/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18381/api/v1/health --expect 200 --status
```

**Expected:** `200` from the Reset, and `200` from `/health` after it.

**This is the defect #482 exists to remove.** Before it, this call answered `409` with obstacle
`SourceUnreadable`, with or without `allowNoBackup=true`, and the only way out was to stop the application
and delete the file by hand. A Reset recreates the database whatever state the old file is in, so it
needs nothing from the file it replaces.

**On failure:** a `409` means the Reset still treats the unreadable file as something to back up first.
A `500` means it reached the file and failed raw.

### 5. Confirm the file was set aside, and no backup was made of it

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-01\backups\unreadable "*.db" 1 --expect-identical-in C:\repos\Quotinator\.claude\temp\qt-backup-01-out\original.db C:\repos\Quotinator\.claude\temp\qt-backup-01\backups\unreadable --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-01\backups "*.db" 0
```

**Expected:** the command exits `0`: exactly one file in `backups/unreadable/`, holding exactly the 34 bytes
step 2 wrote, and no file in `backups/` itself.

**The last check is the one #482's decision rests on.** A file SQLite cannot read is never a backup:
restoring it would put a broken database back in place. Before #482, the refused Reset left the fragment
of its failed copy in `backups/`, where the backup listing offered it as a restore point.

**On failure:** no `unreadable/` folder means the file was deleted or overwritten rather than set aside. A
file in `backups/` itself means something was kept as a backup that must not be.

### 6. Read the notification that says so

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18381/api/v1/notifications --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-01-out\notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-01-out\notifications.json --where items:metadataKind=unreadabledatabasesetaside --length-equals items=1 --contains items.0.body=unreadable
```

**Expected:** exactly one notification of that kind, whose text names the `unreadable` folder.

**On failure:** none means the Reset ran without telling the operator what it set aside, so the file is
somewhere they have no reason to look.

### 7. Reset the now-healthy database, the positive control

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18381/api/v1/admin/database/reset --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-01\backups "*.db" 1 --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-01\backups\unreadable "*.db" 1
dotnet script scripts/testing/http.csx -- --url http://localhost:18381/api/v1/health --expect 200 --status
```

**Expected:** the Reset answers `200`, one backup is now in `backups/`, the reserved folder still holds
only the file step 5 found, and `/health` stays `200`.

**Two things at once.** It is this document's **positive control**: a readable database is still backed
up before a Reset (ADR 025) and is never set aside, so steps 5 and 6 are about the unreadable file and
not about every Reset. And it shows the set-aside file survives a later Reset untouched.

**On failure:** no backup means the readable branch lost its backup. A second file in `unreadable/`
means a readable database was treated as unreadable.

## Observed effect

**Measured 2026-11-09** against `quotinator:local` built from the working tree at `271c4c54`, the database
replaced with 34 bytes of text.

The start degrades to `unhealthy`, and the Reset answers `200` with `/health` back at `200`. The file is
moved, not copied: `backups/unreadable/quotinatordata_unreadable_20261109T084832739Z.db`, 34 bytes and
byte-identical to what step 2 wrote, and no backup of it exists anywhere in `backups/`. The notification
is a `warning` titled *The database file could not be read, and was set aside*, naming that file and the
`unreadable` folder, saying it is not a backup to restore and that it counts towards backup storage, with
no actions offered. The Reset's reseed recommendation sits beside it, as after any Reset.

The control Reset of the rebuilt database took its own backup, `quotinatordata_v9_20261109T084837087Z.db`,
and left the set-aside file where it was.

## Canary

Build the commit before #482's first commit, `69800a23`, as `quotinator:canary-482`, and run steps 1 to 4
against it. Steps 1 to 3 are the control and read the same on both builds: the file is 34 bytes and the
start degrades. Step 4 must answer `409` with obstacle `SourceUnreadable`, which is the refusal this
document exists to remove. `startup-and-degradation/08` and `09`, `backup/02` and `09` share the image.

**Run 2026-11-09** against `69800a23`: steps 1 to 3 passed, and step 4 answered `409` with
`backupObstacle=SourceUnreadable`. Read beyond the steps, the backup folder then held one `.db` file per
refused attempt: the fragment each failed copy left, which the backup listing offers as a restore point.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-01 --bind C:\repos\Quotinator\.claude\temp\qt-backup-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-01 --delete C:\repos\Quotinator\.claude\temp\qt-backup-01-out
```

**Expected:** the container and both directories are gone.
