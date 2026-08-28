# A reset refuses when the database is truncated, not only when it is unrecognisable

**Smoke:** no
**Environment:** Fresh
**Traces to:** #348, #327

## Preconditions

**Beyond the profile.** A real, fully seeded database is cut to half its length while the container is
stopped — a genuine SQLite file with a valid header and missing pages, which is what an interrupted
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
- **The container is stopped before the file is touched**, and stopped *cleanly* by `destroy`, so the
  sidecars are checkpointed away rather than killed mid-write.
- **The second start waits for *listening*, not healthy** — degrading is the expected outcome.

## Steps

### 1. Seed a healthy database and record its size

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-02"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 `
  --image quotinator:local --bind $dataDir

(Invoke-RestMethod "http://localhost:18382/api/v1/version").database.quotes
```

**Expected:** a non-zero quote count.

**On failure:** a fresh seed that cannot reach healthy is not this test's subject. Stop.

### 2. Stop the container and cut the database in half

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-02"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind $dataDir

$path = "$dataDir\quotinatordata.db"
$len  = (Get-Item $path).Length
$fs   = [System.IO.File]::Open($path, 'Open', 'Write')
$fs.SetLength([int64]($len / 2))
$fs.Close()
Remove-Item "$dataDir\quotinatordata.db-wal","$dataDir\quotinatordata.db-shm" -ErrorAction SilentlyContinue
"truncated $len -> $((Get-Item $path).Length)"
```

**Expected:** the reported new length is half the old one, and both are megabytes rather than bytes.

**On failure:** a file-in-use error means the container did not stop — re-run `destroy` and retry.

### 3. Start the current build and attempt a reset

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-02"
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 `
  --image quotinator:local --bind $dataDir --wait-listening

dotnet script scripts/testing/http.csx -- --url "http://localhost:18382/api/v1/health" --expect 503 --status

$r = dotnet script scripts/testing/http.csx -- --url "http://localhost:18382/api/v1/admin/database/reset" `
  --method POST --expect 409 | ConvertFrom-Json
"obstacle=$($r.backupObstacle)"
"offersOverride=$([bool]($r.remedies -match 'allowNoBackup'))"
```

**Expected:** `503` from health, then `409` with `obstacle=SourceUnreadable` and
`offersOverride=False`.

A truncated file reports the same obstacle as an unreadable one, and that is correct rather than a
missed distinction: SQLite cannot read either, so neither can be backed up, and the remedy is the same.
What this document proves is that the *route* to that outcome works for truncation too — which the
unit tests, which use non-database bytes, do not exercise.

**On failure:** a `200` from health means the truncation did not take. A `500` from the reset means the
refusal is not reached on this path even though it is on `01`'s.

### 4. Apply the remedy, and confirm a reset then works

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-02"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind $dataDir

Remove-Item "$dataDir\quotinatordata.db"

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-02 --port 18382 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18382/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
```

**Expected:** healthy, and `200`.

**The positive control.** Step 3 asserts a refusal; on its own that would pass against a build refusing
every reset. This shows the refusal is caused by the truncation and nothing else — and that the remedy
the refusal names actually resolves it.

**On failure:** a `409` means the refusal is not specific to the sabotage, and step 3 proves nothing.

## Observed effect

**Measured 2026-08-28** against `quotinator:local`: a 4,562,944-byte database cut to 2,281,472 bytes
degrades at startup and answers a reset attempt with `409` / `SourceUnreadable`, carrying the same two
remedies as `01` — replace the file, or restore an older backup.

Before #348 this same call returned an unhandled `500`.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-02"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-02 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
