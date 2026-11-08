# A reset refuses when the backup folder cannot be written, and stops re-offering the override

**Smoke:** no
**Environment:** Constrained
**Deterministic:** yes
**Traces to:** #348

## Preconditions

**Beyond the profile.** The data directory is mounted read-only (`--read-only-data`) **after** a normal
run has already created `backups/` inside it. Both halves matter: the folder must already exist, because
this test is about a folder that exists and cannot be written to, which is a different fault from one
that cannot be created, and has a different member of the outcome enum.

## Determinism

- **`backups/` must exist before the read-only run.** Without it, `Directory.CreateDirectory` fails and
  the outcome is `DestinationDirectoryNotWritable`, a different member, covered by a unit test. The
  seeding run in step 1 is what creates it.
- **`--read-only-data`, not `--read-only`.** The root filesystem being unwritable is
  [`startup-and-degradation/04`](../startup-and-degradation/04-migration-replay-under-restricted-write.md)'s
  subject and the application survives it by design.
- **The database must already be migrated.** A read-only mount *plus* a pending migration degrades at
  startup before a reset can be attempted, and that is
  [`startup-and-degradation/05`](../startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md).
  Here the same image seeds and then re-enters, so nothing is pending.
- **Two calls, deliberately.** The second repeats the first *with* the override, because the assertion
  is about what the response says the second time.

## Steps

### 1. Seed a healthy database, creating `backups/`

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-04 --delete C:\repos\Quotinator\.claude\temp\qt-backup-04-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-04\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-04-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-04 --port 18384 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-04
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-directory C:\repos\Quotinator\.claude\temp\qt-backup-04\backups
```

**Expected:** the environment reports healthy, and the `file` command exits `0`: `backups/` exists.

**On failure:** if `backups/` is absent the next step tests the wrong member entirely. Stop.

### 2. Re-enter with the data directory read-only, and attempt a reset

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-04 --port 18384 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-04 --read-only-data --wait-listening
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18384/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset.json --equals backupObstacle=DestinationFileNotWritable --length-equals remedies=2
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset.json "allowNoBackup"
```

**Expected:** `409`, and both TestProbe commands exit `0`: obstacle `DestinationFileNotWritable`, two
remedies, one of them naming `allowNoBackup`.

The override *is* offered here, correctly: at this point nothing has disproved it.

**On failure:** `DestinationDirectoryNotWritable` means `backups/` was not present after all. Re-run
from step 1. A `500` means the refusal is not reached.

### 3. Take the offered override, and read what comes back

```
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18384/api/v1/admin/database/reset?allowNoBackup=true" --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset-override.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset-override.json --equals backupObstacle=DestinationFileNotWritable --length-equals remedies=1
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset-override.json "remedies" --expect-no-text C:\repos\Quotinator\.claude\temp\qt-backup-04-out\reset-override.json "allowNoBackup"
```

**Expected:** `409` again, since the override cannot help, because the data directory the reset itself must
write to is the thing that is read-only: one remedy, and no mention of `allowNoBackup`.

**This is the assertion that matters, and step 2 is its control.** The first response offered a remedy;
the caller took it; it failed. Offering it again would send the operator round the same loop, which is
worse than offering nothing. The same read that found `allowNoBackup` in step 2 finds it absent here, and
one remedy confirms removing it left something actionable behind rather than an empty list.

**On failure:** `allowNoBackup` in the response means the guidance has regressed to repeating advice the
request itself just disproved.

### 4. Remount writable, and confirm a reset then works

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-04 --port 18384 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-04
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18384/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18384/api/v1/health --expect 200 --status
```

**Expected:** healthy, the reset `200`, and `/health` still `200` after it.

**The positive control.** Steps 2 and 3 both assert refusals, so both would hold against a build that
refused every reset, the same container and the same data directory, differing only in the read-only
flag, must succeed. It is also the proof that *"restore write access … then restart"*, the one remedy
left standing after step 3, actually resolves the condition.

**On failure:** a `409` here means the refusal has nothing to do with the mount being read-only, and
steps 2 and 3 prove nothing about it.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `ea9bd594`, in this form and in the
PowerShell form it replaced.

The first attempt answers `409` with `DestinationFileNotWritable` and two remedies: restore write
access, or retry with the override. The second attempt, using that override, answers `409` again with
one remedy: restore write access. The disproved option is gone. Remounted writable, the same directory's
reset answered `200`, after which `/health` still read `200`.

This scenario also found a defect in the pre-flight itself. `Directory.CreateDirectory` on a directory
that already exists is a no-op that succeeds happily on a read-only mount, so the check reported ready
and the reset failed later with `SQLite Error 14` inside the table drop, an unhandled `500`. The check
now writes and deletes a zero-byte probe file, which is the only way to answer the question it claims to
answer.

## Canary

Build `4cfe1006`, the commit before step 8 of
[#348](https://github.com/DutchJaFO/Quotinator/issues/348)'s plan, as `quotinator:canary348`, and run
steps 2 and 3 against it.

Step 2 must fail to report `409` with `obstacle=DestinationFileNotWritable`: an unwritable backup
folder had no refusal of its own before #348. Record whether that build answers `500` or proceeds with
the reset, since the two say different things about what was at risk.

Step 3 is the half this document adds beyond its siblings, and it is where the second reading matters.
The override is offered, taken, and must still be refused, because the directory the reset itself needs
is the one that cannot be written. A build that accepted the override here would have wiped the
database on the strength of an option that could not work.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-04 --bind C:\repos\Quotinator\.claude\temp\qt-backup-04
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-04 --delete C:\repos\Quotinator\.claude\temp\qt-backup-04-out
```

**Expected:** the container and both directories are gone.
