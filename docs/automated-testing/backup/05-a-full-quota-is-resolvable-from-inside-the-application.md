# A full backup quota is resolvable from inside the application

**Smoke:** no
**Environment:** Fresh
**Traces to:** #349

## Preconditions

A normal seeded run on a writable bind mount. Nothing is sabotaged at the filesystem level: the quota is
filled with a real file inside `backups/`, which is the same condition an installation reaches on its own
after enough migrations and resets.

This is the one obstacle of the five that an operator can resolve without leaving the application, and
until #349 there was no way to do it — the remedy named an action with no route. The document exists to
prove the loop closes.

## Determinism

- **The quota, not the ceiling.** Filling to 95% of `MaxBackupStorageGb` puts usage above the 90%
  operating quota while staying below the absolute ceiling. That is the band the reserve occupies, and it
  is what a routine backup refuses on.
- **`--max-backup-storage-gb` stays at its default 1 GB**, so the filler is sized from that. A smaller
  budget cannot be configured — the setting is whole gigabytes.
- **One filler file, sparse.** `SetLength` allocates without writing a gigabyte of content, so the step is
  fast and does not depend on the host's write throughput.
- **The reset in step 5 is the proof, not the point.** A reset is used because it is the action whose
  refusal an operator actually hits; any backup-taking action would do.

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
$key = @{ "X-Api-Key" = "smoke-admin-key" }
$base = "http://localhost:18385/api/v1/admin/backups"

$status = Invoke-RestMethod -Uri "$base/status" -Headers $key
"canBackUp=$($status.canBackUp) used=$($status.storage.usedBytes) quota=$($status.storage.quotaBytes) reserveInUse=$($status.storage.reserveInUse)"

$created = Invoke-RestMethod -Uri "$base/create" -Method POST -Headers $key
"created=$($created.name)"
```

**Expected:** `canBackUp=True`, `reserveInUse=False`, and a created file name.

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
```

**Expected:** `canBackUp=False`, `obstacle=BudgetExceeded`, `reserveInUse=True`, and at least one remedy.

**On failure:** a `canBackUp=True` here means the status endpoint is not measuring against the operating
quota, and step 4's refusal would be for some other reason.

### 4. Confirm a reset now refuses, naming the same obstacle

```powershell
try {
  Invoke-RestMethod -Uri "http://localhost:18385/api/v1/admin/database/reset" -Method POST -Headers $key
} catch {
  $r = $_.ErrorDetails.Message | ConvertFrom-Json
  "status=$($_.Exception.Response.StatusCode.value__) obstacle=$($r.backupObstacle)"
  $r.remedies
}
```

**Expected:** `409`, `BudgetExceeded`, and a remedy list whose first entry names
`GET /api/v1/admin/backups` and `DELETE /api/v1/admin/backups/{name}`.

**The remedy naming the endpoints is the assertion, not decoration.** #348 shipped this text describing an
action the operator had no way to perform; the point of this step is that it now tells them exactly which
call resolves it.

### 5. Apply the remedy through the endpoints, and confirm the reset then works

```powershell
$list = Invoke-RestMethod -Uri $base -Headers $key
$list.items | ForEach-Object { "$($_.name) $($_.sizeBytes)" }

Invoke-RestMethod -Uri "$base/filler.db" -Method DELETE -Headers $key

$status = Invoke-RestMethod -Uri "$base/status" -Headers $key
"canBackUp=$($status.canBackUp) reserveInUse=$($status.storage.reserveInUse)"

dotnet script scripts/testing/http.csx -- --url "http://localhost:18385/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
```

**Expected:** the list includes `filler.db` with its size; the delete returns `204`; status returns to
`canBackUp=True`, `reserveInUse=False`; and the reset returns `200`.

**This closes the loop the issue was filed for** — the obstacle was reached, the message named a remedy,
the remedy was performed through the API alone with no filesystem access, and the action that had refused
now succeeds. Four of the five endpoints are exercised along the way.

### 6. Confirm the download returns the backup byte for byte

```powershell
$name = (Invoke-RestMethod -Uri $base -Headers $key).items[0].name
Invoke-WebRequest -Uri "$base/$name/content" -Headers $key -OutFile "$dataDir\downloaded.db"

$a = Get-FileHash "$dataDir\backups\$name" -Algorithm SHA256
$b = Get-FileHash "$dataDir\downloaded.db"  -Algorithm SHA256
"match=$($a.Hash -eq $b.Hash)"
```

**Expected:** `match=True`.

**On failure:** a downloaded backup that does not match the stored file is not a restore point, whatever
the status code said.

## Observed effect

_Not yet run against a built image — this document is written with #349 and awaits its T2 pass._

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-05"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-05 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
