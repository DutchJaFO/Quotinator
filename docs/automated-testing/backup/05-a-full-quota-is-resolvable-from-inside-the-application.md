# A full backup quota is resolvable from inside the application

**Smoke:** no
**Environment:** Fresh
**Traces to:** #349

## Preconditions

A normal seeded run on a writable bind mount. Nothing is sabotaged at the filesystem level until the
final step: the quota is filled with a real file inside `backups/`, which is the same condition an
installation reaches on its own after enough migrations and resets.

This is the one obstacle of the five that an operator can resolve without leaving the application, and
until #349 there was no way to do it — the remedy named an action with no route. The document exists to
prove the loop closes.

## Determinism

- **The quota, not the ceiling.** Filling to 95% of `MaxBackupStorageGb` puts usage above the 90%
  operating quota while staying below the absolute ceiling. That is the band the reserve occupies, and it
  is what a routine backup refuses on.
- **`MaxBackupStorageGb` stays at its default 1 GB**, so the filler is sized from that. A smaller budget
  cannot be configured — the setting is whole gigabytes.
- **One filler file, sparse.** `SetLength` allocates without writing a gigabyte of content, so the step is
  fast and does not depend on the host's write throughput.
- **The stored backup is readable from the host, and that is itself part of what this document
  proves.** It was not, on the first run: the connection that writes a backup was pooled, so its file
  handle survived disposal and the file stayed locked for the life of the process. The first version of
  this document worked around it by hashing inside the container and blamed the host filesystem — the
  cause was ours, and it is fixed at source (`Pooling=false` on the destination connection). Hashing
  from the host now is the check that the leak has not returned.
- **`$_.ErrorDetails.Message` is null in Windows PowerShell 5.1** for these responses. The body is read
  off the exception's response stream instead; a snippet relying on `ErrorDetails` reports an empty
  obstacle and looks like a failure of the endpoint.

## Steps

### 1. Seed a healthy database

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-05"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$dataDir\backups" | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-05 --port 18385 `
  --image quotinator:local --bind $dataDir
```

**Expected:** the environment reports healthy.

### 2. Confirm status says a backup is possible, and take one

```powershell
$key  = @{ "X-Api-Key" = "smoketest" }
$base = "http://localhost:18385/api/v1/admin/backups"

$status = Invoke-RestMethod -Uri "$base/status" -Headers $key
"canBackUp=$($status.canBackUp) used=$($status.storage.usedBytes) quota=$($status.storage.quotaBytes) reserveInUse=$($status.storage.reserveInUse)"

$created = Invoke-RestMethod -Uri "$base/create" -Method POST -Headers $key
"created=$($created.name) size=$($created.sizeBytes)"
```

**Expected:** `canBackUp=True`, `used=0`, `reserveInUse=False`, then a created file name with a non-zero
size.

**This is the positive control for the whole document.** Everything below asserts a refusal, and a build
that refused every backup would satisfy all of it without this step.

**On failure:** if create refuses here the environment is not clean, and nothing below is measuring the
quota.

### 3. Fill the quota, and confirm status now says a backup is not possible

```powershell
$filler = Join-Path $dataDir "backups\filler.db"
$stream = [System.IO.File]::Create($filler)
$stream.SetLength([int64](1073741824 * 0.95))
$stream.Close()

$status = Invoke-RestMethod -Uri "$base/status" -Headers $key
"canBackUp=$($status.canBackUp) obstacle=$($status.obstacle) reserveInUse=$($status.storage.reserveInUse) remedies=$($status.remedies.Count)"
$status.remedies
```

**Expected:** `canBackUp=False`, `obstacle=BudgetExceeded`, `reserveInUse=True`, and three remedies, the
first of which names `GET /api/v1/admin/backups` and `DELETE /api/v1/admin/backups/{name}`.

**On failure:** a `canBackUp=True` here means the status endpoint is not measuring against the operating
quota, and step 4's refusal would be for some other reason.

### 4. Confirm a reset now refuses, naming the same obstacle

```powershell
try {
  Invoke-WebRequest -Uri "http://localhost:18385/api/v1/admin/database/reset" -Method POST -Headers $key -UseBasicParsing
} catch {
  $resp   = $_.Exception.Response
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
  $body   = $reader.ReadToEnd(); $reader.Close()
  "status=$($resp.StatusCode.value__)"
  ($body | ConvertFrom-Json).backupObstacle
}
```

**Expected:** `409` and `BudgetExceeded`.

**The remedy naming the endpoints is the assertion, not decoration.** #348 shipped this text describing an
action the operator had no way to perform; the point of this step is that it now tells them exactly which
call resolves it.

### 5. Apply the remedy through the endpoints, and confirm the reset then works

```powershell
$list = Invoke-RestMethod -Uri $base -Headers $key
"totalCount=$($list.totalCount)"
$list.items | ForEach-Object { "  $($_.name) $($_.sizeBytes)" }

Invoke-RestMethod -Uri "$base/filler.db" -Method DELETE -Headers $key

$after = Invoke-RestMethod -Uri "$base/status" -Headers $key
"canBackUp=$($after.canBackUp) reserveInUse=$($after.storage.reserveInUse)"

dotnet script scripts/testing/http.csx -- --url "http://localhost:18385/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
```

**Expected:** the list reports both files with their sizes; the delete returns `204`; status returns to
`canBackUp=True`, `reserveInUse=False`; and the reset returns `200`.

**This closes the loop the issue was filed for** — the obstacle was reached, the message named a remedy,
the remedy was performed through the API alone with no filesystem access, and the action that had refused
now succeeds.

### 6. Confirm the download returns the backup byte for byte

```powershell
$name = (Invoke-RestMethod -Uri $base -Headers $key).items[0].name
Invoke-WebRequest -Uri "$base/$name/content" -Headers $key -OutFile "$dataDir\downloaded.db" -UseBasicParsing

$a = (Get-FileHash "$dataDir\backups\$name" -Algorithm SHA256).Hash
$b = (Get-FileHash "$dataDir\downloaded.db"  -Algorithm SHA256).Hash
"match=$($a -eq $b)"
```

**Expected:** `match=True`.

**Two assertions in one.** The hashes matching says the download is byte-for-byte. Reading the *stored*
file from the host at all says no handle is still held on it — the leak that made an immediate download
fail with an unhandled `500` in T1. A `Get-FileHash` that cannot open the stored file is that leak
returning, not a test problem.

**On failure:** a downloaded backup that does not match the stored file is not a restore point, whatever
the status code said.

### 7. Confirm a removal the filesystem refuses says so, rather than failing raw

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-05 --port 18385 `
  --image quotinator:local --bind $dataDir --read-only-data --wait-listening

$name = (Invoke-RestMethod -Uri $base -Headers $key).items[0].name
try {
  Invoke-WebRequest -Uri "$base/$name" -Method DELETE -Headers $key -UseBasicParsing
} catch {
  $resp   = $_.Exception.Response
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
  $body   = $reader.ReadToEnd(); $reader.Close()
  "status=$($resp.StatusCode.value__)"
  ($body | ConvertFrom-Json).detail
}
```

**Expected:** `409`, and a detail saying the backup exists but could not be removed because the data
directory is read-only.

**This is the case the endpoint is most likely to meet in anger** — a read-only mount is what degrades
startup in the first place, and removing old backups is what the operator is then told to do. The
listing still answers on the same mount, so the operator can see what is there even while unable to
remove it.

## Observed effect

**Measured 2026-08-29** against `quotinator:local`.

The loop closes exactly as intended. A fresh install reports `canBackUp=True` with `used=0` against a
`966,367,641`-byte quota beneath a `1,073,741,824`-byte ceiling; `create` writes a real 4.5 MB backup and
names it. Filling to 95% flips status to `canBackUp=False`/`BudgetExceeded` with `reserveInUse=True`, and
a reset then answers `409` with the same obstacle. Listing, deleting the filler, and re-checking status
restores `canBackUp=True`, and the reset returns `200`. The downloaded backup's SHA-256 matches the
container's own hash of the stored file.

**This pass found three defects in this document and one in the application.**

The document named the wrong admin key, used `$_.ErrorDetails.Message` (null in Windows PowerShell 5.1,
so the obstacle read as empty), and worked around a locked backup file by hashing inside the container —
attributing the lock to the host filesystem. That third one was wrong twice over: the lock was ours, and
calling it a host property hid a real defect for a day. See the T1 note below.

**Re-run 2026-08-29, after the handle leak was fixed.** Creating a backup and downloading it immediately
now returns `200`, and the stored file is readable from the host, which it was not before — so step 6
hashes it directly rather than reaching into the container.

**A second application defect was found in T1, which this T2 pass had missed entirely.** Downloading a
backup created moments earlier answered an unhandled `500`: `Microsoft.Data.Sqlite` pools connections by
default, so the destination connection that writes a backup returned to the pool on disposal and kept
its file handle open for the life of the process. Every backup this application had ever written stayed
locked. It is invisible on Unix — a retained handle blocks neither a second open nor an unlink — which
is exactly why running this document in a Linux container could not catch it, and why the workaround
above looked like a host quirk instead of evidence. The destination connection now opts out of pooling.

The other application defect is step 7's subject, and it was not in the plan. `DELETE` against a read-only data
directory answered an **unhandled `500`**: `File.Delete` threw, nothing caught it. That is precisely the
defect class #348 was filed to remove, reintroduced one endpoint over, on the single path an operator is
most likely to take — the read-only mount that degraded their startup is the same one that refuses the
removal they were just told to perform. The writer now reports a typed outcome instead of throwing, and
the endpoint answers `409` naming the condition and its remedy.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-05"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-05 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
