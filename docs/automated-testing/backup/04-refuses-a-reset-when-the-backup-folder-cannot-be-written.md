# A reset refuses when the backup folder cannot be written, and stops re-offering the override

**Smoke:** no
**Environment:** Constrained
**Traces to:** #348

## Preconditions

**Beyond the profile.** The data directory is mounted read-only (`--read-only-data`) **after** a normal
run has already created `backups/` inside it. Both halves matter: the folder must already exist, because
this test is about a folder that exists and cannot be written to — which is a different fault from one
that cannot be created, and has a different member of the outcome enum.

## Determinism

- **`backups/` must exist before the read-only run.** Without it, `Directory.CreateDirectory` fails and
  the outcome is `DestinationDirectoryNotWritable` — a different member, covered by a unit test. The
  seeding run in step 1 is what creates it.
- **`--read-only-data`, not `--read-only`.** The root filesystem being unwritable is
  [`startup-and-degradation/04`](../startup-and-degradation/04-migration-replay-under-restricted-write.md)'s
  subject and the application survives it by design.
- **The database must already be migrated.** A read-only mount *plus* a pending migration degrades at
  startup before a reset can be attempted — that is
  [`startup-and-degradation/05`](../startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md).
  Here the same image seeds and then re-enters, so nothing is pending.
- **Two calls, deliberately.** The second repeats the first *with* the override, because the assertion
  is about what the response says the second time.

## Steps

### 1. Seed a healthy database, creating `backups/`

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-04"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$dataDir\backups" | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-04 --port 18384 `
  --image quotinator:local --bind $dataDir

Test-Path "$dataDir\backups"
```

**Expected:** `True`, and the environment reports healthy.

**On failure:** if `backups/` is absent the next step tests the wrong member entirely. Stop.

### 2. Re-enter with the data directory read-only, and attempt a reset

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-04"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-04 --port 18384 `
  --image quotinator:local --bind $dataDir --read-only-data --wait-listening

$r = dotnet script scripts/testing/http.csx -- --url "http://localhost:18384/api/v1/admin/database/reset" `
  --method POST --expect 409 | ConvertFrom-Json
"obstacle=$($r.backupObstacle)"
"offersOverride=$([bool]($r.remedies -match 'allowNoBackup'))"
```

**Expected:** `409`, `obstacle=DestinationFileNotWritable`, and `offersOverride=True`.

The override *is* offered here, correctly: at this point nothing has disproved it.

**On failure:** `DestinationDirectoryNotWritable` means `backups/` was not present after all — re-run
from step 1. A `500` means the refusal is not reached.

### 3. Take the offered override, and read what comes back

```powershell
$r2 = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18384/api/v1/admin/database/reset?allowNoBackup=true" `
  --method POST --expect 409 | ConvertFrom-Json
"obstacle=$($r2.backupObstacle)"
"offersOverride=$([bool]($r2.remedies -match 'allowNoBackup'))"
"remedyCount=$($r2.remedies.Count)"
```

**Expected:** `409` again — the override cannot help, because the data directory the reset itself must
write to is the thing that is read-only — with `offersOverride=False` and `remedyCount` of 1.

**This is the assertion that matters.** The first response offered a remedy; the caller took it; it
failed. Offering it again would send the operator round the same loop, which is worse than offering
nothing. `remedyCount=1` confirms removing it left something actionable behind rather than an empty list.

**On failure:** `offersOverride=True` means the guidance has regressed to repeating advice the request
itself just disproved.

### 4. Remount writable, and confirm a reset then works

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-04"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-04 --port 18384 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18384/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
```

**Expected:** healthy, and `200`.

**The positive control.** Steps 2 and 3 both assert refusals, so both would hold against a build that
refused every reset — the same container and the same data directory, differing only in the read-only
flag, must succeed. It is also the proof that *"restore write access … then restart"*, the one remedy
left standing after step 3, actually resolves the condition.

**On failure:** a `409` here means the refusal has nothing to do with the mount being read-only, and
steps 2 and 3 prove nothing about it.

## Observed effect

**Measured 2026-08-28** against `quotinator:local`.

The first attempt answers `409` with `DestinationFileNotWritable` and two remedies — restore write
access, or retry with the override. The second attempt, using that override, answers `409` again with
one remedy: restore write access. The disproved option is gone.

This scenario also found a defect in the pre-flight itself. `Directory.CreateDirectory` on a directory
that already exists is a no-op that succeeds happily on a read-only mount, so the check reported ready
and the reset failed later with `SQLite Error 14` inside the table drop — an unhandled `500`. The check
now writes and deletes a zero-byte probe file, which is the only way to answer the question it claims to
answer.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-04"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-04 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
