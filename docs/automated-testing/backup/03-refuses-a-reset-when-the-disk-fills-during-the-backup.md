# A reset refuses when the disk fills during the backup, instead of wiping behind a 200

**Smoke:** no
**Environment:** Fresh
**Traces to:** #348

## Preconditions

**Beyond the profile.** The data directory is a **tmpfs with a hard size ceiling** rather than a volume
or a bind mount, so the space available to a backup is a property of the test rather than of whatever the
host happens to have free. The ceiling is deliberately roomy: the test seeds into it normally, then fills
the space it does not want, computing that from what it measures rather than from a figure written here.

**This is the one case the pre-flight cannot catch**, and that is the point of testing it. The check
sees free space before the copy starts; the copy is what exhausts it.

## Determinism

- **No size is calibrated against the dataset.** The condition is that the volume has less room than the
  copy needs, and the test establishes it by measuring: it reads the free space and the database's own
  size, then writes a filler leaving half the database's size free. Whatever the dataset grows to, the
  free space left is always half of what a copy costs. The tmpfs ceiling is only "comfortably larger than
  a seed", not a calibrated value, and nothing below asserts a byte count.
- **This replaces two figures that went stale twice** (2026-09-28). The ceiling had been calibrated so a
  seed just fitted and a copy just did not, and both halves of that drifted: a copy stopped being smaller
  than its source, because this database no longer carries free pages for SQLite to skip, and the free
  space swung by megabytes with the size of the write-ahead log, reading 3,352 KB and 3,272 KB on two
  successive runs of the same image. Each drift was caught only by a reader noticing a stale number, which
  is exactly what a derived value removes.
- **A copy costs what the database costs**, so leaving half of it free is a whole copy short. It is also
  comfortably more than the backups folder and the backup file itself need to be created, which must
  succeed for the copy to be the thing that fails.
- **tmpfs, not a bind mount.** A bind mount inherits the host filesystem's free space, which no test can
  control; filling a real disk to provoke this would be both slow and hostile to the machine running it.
- **The data does not survive the container**, which is acceptable here only because this test never
  restarts: it seeds, attempts one reset, and asserts. A scenario needing its database across a restart
  must not use this flag.
- **The quote count is read before and after.** The failure this guards against reported success while
  destroying data, so "it refused" is not enough on its own: the data has to still be there.

## Steps

### 1. Seed into a size-capped data directory

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-03 --port 18383 `
  --image quotinator:local --tmpfs-data 64m

$before = (Invoke-RestMethod "http://localhost:18383/api/v1/version").database.quotes
"quotesBefore=$before"
```

**Expected:** the environment reports healthy and `quotesBefore` is non-zero.

**The ceiling is not calibrated against the dataset**, only comfortably larger than a seed, so growth in
the bundled content cannot silently make this step fail or the next one stop testing anything.

**On failure:** a seed that cannot complete is not this test's subject. If it ever outgrows the ceiling,
raise it: no assertion below depends on its value.

### 2. Leave less room than a copy needs, measured rather than assumed

```powershell
$avail = [int](((docker exec qt-backup-03 df -k /data | Select-Object -Last 1) -split '\s+')[3])
$dbKB  = [int]((docker exec qt-backup-03 du -k /data/quotinatordata.db) -split '\s+')[0]
$spare = [int]($dbKB / 2)

docker exec qt-backup-03 dd if=/dev/zero of=/data/filler.bin bs=1024 count=$($avail - $spare) 2>$null
$left = [int](((docker exec qt-backup-03 df -k /data | Select-Object -Last 1) -split '\s+')[3])
"leftKB=$left dbKB=$dbKB roomForACopy=$($left -ge $dbKB)"
```

**Expected:** `roomForACopy=False`.

**Nothing here is a figure this document chose.** A copy costs what the database costs, so leaving half
of that free is always a whole copy short, whatever the dataset has grown to. The two numbers printed are
evidence, not the gate: the gate is `roomForACopy`.

**On failure:** `roomForACopy=True` means the fill did not take, and the backup below would succeed, so
the test would pass without ever reaching its own condition.

### 3. Attempt a reset, which must refuse

```powershell
$r = dotnet script scripts/testing/http.csx -- --url "http://localhost:18383/api/v1/admin/database/reset" `
  --method POST --expect 409 | ConvertFrom-Json
"obstacle=$($r.backupObstacle)"
```

**Expected:** `409` with `obstacle=DiskFilledDuringBackup`.

**On failure:** a `200` is the exact regression this document exists to catch. See Observed effect.
A `500` means the failure escaped unhandled instead of being reported.

### 4. Confirm the database is still there

```powershell
$after = (Invoke-RestMethod "http://localhost:18383/api/v1/version").database.quotes
"quotesAfter=$after"
```

**Expected:** the same non-zero count as step 1.

**On failure:** a count of `0` means the reset ran despite refusing: the database was destroyed and the
only restore point is a truncated fragment. This assertion is the substantive one; step 2's status code
alone would not catch it.

### 5. Give it room back, and confirm a reset then works

```powershell
docker exec qt-backup-03 rm /data/filler.bin

dotnet script scripts/testing/http.csx -- --url "http://localhost:18383/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
```

**Expected:** `200`.

**The remedy is applied to the same container**, so the only thing that changed between the refusal and
the success is the space the filler was occupying. A second container at a different ceiling would have
left the size of the mount as another difference, and this step exists to rule every other difference out.

**The positive control, and the remedy.** Steps 3 and 4 assert a refusal and untouched data; both would
hold against a build that refused every reset. Removing the filler must make the same call succeed, which
is also what proves step 3's stated remedy, *"free disk space and retry"*, is real advice.

**On failure:** a `409` with the filler gone means the refusal is not caused by the space at all, and this
document's whole premise is wrong. Investigate rather than giving it more room until it passes.

## Observed effect

**Rewritten and re-measured 2026-09-28** against `quotinator:local`. The behaviour is unchanged, the
reset refuses with `DiskFilledDuringBackup` and the quotes are still there, but the document no longer
calibrates anything against the dataset. It had held two figures that both went stale: a copy stopped
being smaller than its source, because this database no longer carries free pages for SQLite to skip, and
the free space swung by megabytes with the size of the write-ahead log. The ceiling sized to make a seed
just fit and a copy just not fit was drifting on both sides at once, and each drift surfaced only when a
reader noticed a number that no longer matched.

The condition is now established by measurement: read the free space and the database's own size, fill
all but half the latter, and a copy is always a whole copy short. The database read 4,596 KB and then
4,552 KB across two runs in the same session, which is the point. Nothing asserts a byte count, and the
positive control removes the filler from the same container, so the space is the only thing that differs
between the refusal and the success.

**Why the pre-flight does not catch this**, established while rewriting: the application reads free space
for the backups folder as roughly a terabyte on a tmpfs mount, the host drive rather than the mount, so
`InsufficientDiskSpace` cannot fire here however little room is left. The copy meets the real limit
instead. That is what makes this the one case the pre-flight cannot catch, rather than a matter of
timing, and it is worth knowing before anyone tries to provoke `InsufficientDiskSpace` the same way.

**Measured 2026-08-28** against `quotinator:local`, and this document exists because the first
measurement found something worse than the bug it was written for.

**Before the fix**, this exact scenario returned **`200 OK`**. The log showed
`[Database - Backup] backing up v5 → …` with no completion line, the backup file on disk was exactly the
1,380,352 bytes that had been free, truncated, and the reset went on to drop every table and rebuild.
`DropAndRebuildAsync` had taken the backup result and read only its path, never whether it succeeded.

So the operator would have been told their reset worked, with their data gone and the only restore point
an unusable fragment. That is strictly worse than the unhandled `500` #348 set out to remove, because it
looks like success.

**After the fix**, the same call answers:

> `409 Conflict`, `backupObstacle: DiskFilledDuringBackup`
> *"The volume ran out of space partway through writing the backup, after the pre-flight check had
> passed."*
> Remedies: free disk space and retry; remove the partially written backup file if one was left behind.

and the quote count is unchanged: 799 before, 799 after.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-03
```
