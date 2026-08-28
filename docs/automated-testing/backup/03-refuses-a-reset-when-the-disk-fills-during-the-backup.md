# A reset refuses when the disk fills during the backup, instead of wiping behind a 200

**Smoke:** no
**Environment:** Fresh
**Traces to:** #348

## Preconditions

**Beyond the profile.** The data directory is a **tmpfs with a hard size ceiling** rather than a volume
or a bind mount, so the space available to a backup is a property of the test rather than of whatever
the host happens to have free. `--tmpfs-data 11m` is sized so a full seed fits and a backup of the
resulting database does not.

**This is the one case the pre-flight cannot catch**, and that is the point of testing it. The check
sees free space before the copy starts; the copy is what exhausts it.

## Determinism

- **11 MB, measured rather than guessed.** At 12 MB the backup *succeeds* — the copy came to 2.1 MB
  against a 4.5 MB source file, because SQLite copies pages and a file carries free ones. That is the
  same unpredictability the operating quota exists for, and it is why this size is stated as a measured
  value: change the bundled dataset and it must be re-measured, not adjusted by feel.
- **tmpfs, not a bind mount.** A bind mount inherits the host filesystem's free space, which no test can
  control; filling a real disk to provoke this would be both slow and hostile to the machine running it.
- **The data does not survive the container**, which is acceptable here only because this test never
  restarts: it seeds, attempts one reset, and asserts. A scenario needing its database across a restart
  must not use this flag.
- **The quote count is read before and after.** The failure this guards against reported success while
  destroying data, so "it refused" is not enough on its own — the data has to still be there.

## Steps

### 1. Seed into a size-capped data directory

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-03 --port 18383 `
  --image quotinator:local --tmpfs-data 11m --wait-listening

docker exec qt-backup-03 df -h /data
$before = (Invoke-RestMethod "http://localhost:18383/api/v1/version").database.quotes
"quotesBefore=$before"
```

**Expected:** the seed completes, `df` reports roughly 1–2 MB available of 11 MB, and
`quotesBefore` is non-zero.

**On failure:** if `df` shows several MB free, the ceiling is too generous and the backup will succeed —
the test would pass without ever reaching its own condition. If the seed itself fails, the ceiling is
too tight. Either way, re-measure rather than proceeding.

### 2. Attempt a reset, which must refuse

```powershell
$r = dotnet script scripts/testing/http.csx -- --url "http://localhost:18383/api/v1/admin/database/reset" `
  --method POST --expect 409 | ConvertFrom-Json
"obstacle=$($r.backupObstacle)"
```

**Expected:** `409` with `obstacle=DiskFilledDuringBackup`.

**On failure:** a `200` is the exact regression this document exists to catch — see Observed effect.
A `500` means the failure escaped unhandled instead of being reported.

### 3. Confirm the database is still there

```powershell
$after = (Invoke-RestMethod "http://localhost:18383/api/v1/version").database.quotes
"quotesAfter=$after"
```

**Expected:** the same non-zero count as step 1.

**On failure:** a count of `0` means the reset ran despite refusing — the database was destroyed and the
only restore point is a truncated fragment. This assertion is the substantive one; step 2's status code
alone would not catch it.

## Observed effect

**Measured 2026-08-28** against `quotinator:local`, and this document exists because the first
measurement found something worse than the bug it was written for.

**Before the fix**, this exact scenario returned **`200 OK`**. The log showed
`[Database - Backup] backing up v5 → …` with no completion line, the backup file on disk was exactly the
1,380,352 bytes that had been free — truncated — and the reset went on to drop every table and rebuild.
`DropAndRebuildAsync` had taken the backup result and read only its path, never whether it succeeded.

So the operator would have been told their reset worked, with their data gone and the only restore point
an unusable fragment. That is strictly worse than the unhandled `500` #348 set out to remove, because it
looks like success.

**After the fix**, the same call answers:

> `409 Conflict` — `backupObstacle: DiskFilledDuringBackup`
> *"The volume ran out of space partway through writing the backup, after the pre-flight check had
> passed."*
> Remedies: free disk space and retry; remove the partially written backup file if one was left behind.

and the quote count is unchanged — 799 before, 799 after.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-03
```
