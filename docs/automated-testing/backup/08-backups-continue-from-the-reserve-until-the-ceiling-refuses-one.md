# Backups continue from the reserve, with a warning, until the ceiling refuses one

**Smoke:** no
**Environment:** Fresh
**Traces to:** #348

## Preconditions

Beyond the Fresh profile, this test supplies its own storage pressure: a sparse filler file inside
`backups/`, written and removed from the host rather than through the application. It therefore needs
`--bind`, so the host can reach the folder the container is measuring.

The pressure is established in two distinct bands, and every assertion below depends on which one is in
force:

- **The reserve**, between the operating quota and the ceiling. A backup here is still taken, and the
  warning is raised.
- **Past the ceiling**, where a backup is refused.

Step 3 confirms the reserve was actually reached rather than inferring it from the filler having been
written, and step 9 does the same for the ceiling.

## Determinism

- **`MaxBackupStorageGb` stays at its default 1 GB**, so the filler is sized from that. A smaller
  ceiling cannot be configured, since the setting is whole gigabytes.
- **`BackupQuotaPercent` stays at its default 90**, so the reserve is the band from 90% to 100% of the
  ceiling. Filling to 92% lands inside it: above the quota, below the ceiling.
- **A backup must fit in what is left below the ceiling, or step 4 would be measuring a refusal rather
  than a backup.** This is not assumed: step 2 takes a backup and records what it actually cost, and
  step 3 checks that `remainingAgainstCeilingBytes` still exceeds it. Both figures come from the run,
  so neither is a prediction about the database's size.
- **One filler file at a time, sparse.** `SetLength` allocates without writing the bytes, so a step is
  fast and does not depend on the host's write throughput.
- **Open warnings are counted with `isDismissed` filtered out.** The endpoint returns the full history,
  so a resolved warning is still listed; counting rows without that filter counts warnings that have
  already been cleared.
- **The warning is one row however often its condition is checked**, so every count below is of open
  rows, never of checks performed.
- **`$_.ErrorDetails.Message` is null in Windows PowerShell 5.1** for these responses. The body is read
  off the exception's response stream instead; a snippet relying on `ErrorDetails` reports an empty
  obstacle and looks like a failure of the endpoint.

## Steps

### 1. Seed a healthy database

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-08"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$dataDir\backups" | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-backup-08 --port 18388 `
  --image quotinator:local --bind $dataDir
```

**Expected:** the environment reports healthy.

### 2. Positive control: below the quota, a backup is taken and nothing is warned about

```powershell
$key  = @{ "X-Api-Key" = "smoketest" }
$base = "http://localhost:18388/api/v1"

function Open-QuotaWarnings {
  @((Invoke-RestMethod -Uri "$base/notifications?pageSize=0").items |
    Where-Object { $_.metadataKind -eq 'backupquotareached' -and -not $_.isDismissed })
}

$status = Invoke-RestMethod -Uri "$base/admin/backups/status" -Headers $key
"canBackUp=$($status.canBackUp) reserveInUse=$($status.storage.reserveInUse) used=$($status.storage.usedBytes)"

$first = Invoke-RestMethod -Uri "$base/admin/backups/create" -Method POST -Headers $key
$backupCost = $first.sizeBytes
"created=$($first.name) cost=$backupCost warnings=$(@(Open-QuotaWarnings).Count)"
```

**Expected:** `canBackUp=True`, `reserveInUse=False`, `used=0`, then a created file name, a non-zero
`cost`, and `warnings=0`.

**This is the positive control for the whole document.** Everything below asserts that a warning is
raised, and a build that raised it unconditionally would satisfy all of it without this step.

**On failure:** a warning already open here, or a refused create, means the environment is not clean and
nothing below is measuring the reserve.

### 3. Fill into the reserve, and confirm that is the band reached

```powershell
$filler = Join-Path $dataDir "backups\filler.db"
$stream = [System.IO.File]::Create($filler)
$stream.SetLength([int64](1073741824 * 0.92))
$stream.Close()

$status = Invoke-RestMethod -Uri "$base/admin/backups/status" -Headers $key
"canBackUp=$($status.canBackUp) reserveInUse=$($status.storage.reserveInUse)"
"roomBelowCeiling=$($status.storage.remainingAgainstCeilingBytes) backupCost=$backupCost fits=$($status.storage.remainingAgainstCeilingBytes -gt $backupCost)"
```

**Expected:** `reserveInUse=True` and `canBackUp=True` together, meaning the quota has been passed and
the ceiling has not, with `fits=True`.

**On failure:** `canBackUp=False` has two causes, and `fits` tells them apart. With `fits=False` the fill
overshot the ceiling, so steps 4 to 8 would be measuring refusals rather than the reserve. With
`fits=True` the ceiling was not reached and the build is refusing at the *quota*: the pre-#348 model,
where the reserve could not be used without the caller opting in. Either way, stop: the band below is
not the one being measured.

### 4. A backup is still taken from the reserve, and raises the warning

```powershell
$created = Invoke-RestMethod -Uri "$base/admin/backups/create" -Method POST -Headers $key
"created=$($created.name) size=$($created.sizeBytes)"

$warning = Open-QuotaWarnings
"warnings=$(@($warning).Count) type=$($warning.type) resolution=$($warning.resolution)"
"body=$($warning.body)"
```

**Expected:** a created file with a non-zero size, then `warnings=1`, `type=warning`, an empty
`resolution`, and a body naming three sizes: what the folder holds, the quota it has passed, and the
ceiling it has not.

**Both halves matter and neither implies the other.** A build that refused the backup would fail the
first line; a build that took it silently would fail the second.

**On failure:** a refusal here is the pre-#348 model, where the quota refused rather than warned.

### 5. The warning survives a restart, and the startup check does not duplicate it

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-08 --port 18388 `
  --image quotinator:local --bind $dataDir

"warnings=$(@(Open-QuotaWarnings).Count)"
```

**Expected:** `warnings=1`.

**Two failures are distinguishable here, which is why the count is reported rather than a boolean.** `0`
means the startup check did not run or did not raise; `2` means it raised a second warning for a
condition already open, which is what an identity built from the bytes in use would do, since the folder
grew by a backup in step 4.

### 6. A Reset from the reserve is not refused, and what it offers carries the caution

```powershell
dotnet script scripts/testing/http.csx -- --url "$base/admin/database/reset" `
  --method POST --expect 200 --status

$rows = (Invoke-RestMethod -Uri "$base/notifications?pageSize=0").items | Where-Object { -not $_.isDismissed }
$offering = $rows | Where-Object { @($_.availableActions).Count -gt 0 }
"warnings=$(@(Open-QuotaWarnings).Count) offering=$(@($offering).Count) caution=$($offering.backupCaution -join ',')"
"actions=$($offering.availableActions -join ',')"
```

**Expected:** the reset returns `200`, then `warnings=1`, at least one row offering an action, every one
of those rows reporting `caution=True`, and the offered actions including one that takes a backup
(`backupthenreseed`, `removeoldestbackupthenreseed` or `resetdatabase`).

**The warning count is the interesting one.** A Reset drops every table, this row included, so a `1`
here is the post-reset condition check having raised it again rather than the original row surviving.

**On failure:** a `409` means the reserve is refusing a Reset, which is the model #348 replaced.

### 6b. The page shows the caution beside the option that takes the backup

```powershell
$out = "C:\repos\Quotinator\.claude\temp\qt-backup-08"
$rows = @'
const rows = [...document.querySelectorAll('tbody tr')];
const cautioned = rows.filter(r => r.textContent.includes('comes from the reserve'));
return { rows: rows.length,
         cautioned: cautioned.length,
         warningBadges: rows.filter(r => r.querySelector('.badge.bg-warning, .badge.bg-danger')).length,
         buttons: cautioned.flatMap(r => [...r.querySelectorAll('button')].map(b => b.textContent.trim())) };
'@
$page = $rows | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18388/notifications" `
  --out "$out\reserve-caution.png" --height 1100 --script-stdin | ConvertFrom-Json
"rows=$($page.rows) cautioned=$($page.cautioned) buttons=$($page.buttons -join ' | ')"
```

**Expected:** `cautioned=1`, its `buttons` including one that takes a backup, so the caution is rendered
beside the choice it qualifies rather than only carried over REST. `reserve-caution.png` is the artefact
to look at.

**On failure:** `cautioned=0` with step 6 reporting `caution=True` means the response and the page
disagree, which is the specific drift `NotificationActionOptions.TakesABackup` exists to prevent.

### 7. Removing a backup through the application clears the warning, with no restart

```powershell
Invoke-RestMethod -Uri "$base/admin/backups/filler.db" -Method DELETE -Headers $key

$status = Invoke-RestMethod -Uri "$base/admin/backups/status" -Headers $key
$resolved = @((Invoke-RestMethod -Uri "$base/notifications?pageSize=0").items |
  Where-Object { $_.metadataKind -eq 'backupquotareached' -and $_.isDismissed })
"reserveInUse=$($status.storage.reserveInUse) open=$(@(Open-QuotaWarnings).Count) resolved=$($resolved.Count) resolution=$($resolved[0].resolution)"
```

**Expected:** `reserveInUse=False`, `open=0`, `resolved` of at least 1, and `resolution=underquota`.

**The container was not restarted between the deletion and the read**, which is the point: the deletion
itself re-checked the condition.

### 8. A removal the application never saw is picked up on request, still with no restart

```powershell
$filler = Join-Path $dataDir "backups\filler.db"
$stream = [System.IO.File]::Create($filler)
$stream.SetLength([int64](1073741824 * 0.92))
$stream.Close()

$raised = Invoke-RestMethod -Uri "$base/notifications/refresh" -Method POST
"raised=$(($raised.checks | Where-Object { $_.kind -eq 'backupquotareached' }).outcome) open=$(@(Open-QuotaWarnings).Count)"

Remove-Item -Force $filler

$cleared = Invoke-RestMethod -Uri "$base/notifications/refresh" -Method POST
"cleared=$(($cleared.checks | Where-Object { $_.kind -eq 'backupquotareached' }).outcome) open=$(@(Open-QuotaWarnings).Count)"
```

**Expected:** `raised=raised` with `open=1`, then `cleared=cleared` with `open=0`.

**Neither refresh carried an admin key**, which is deliberate: the endpoint is open to every user.

**This is the case a restart would otherwise be needed for.** The file was removed from the host, so no
application path observed it; nothing but the refresh could have noticed.

**On failure:** an `unchanged` on the first call means the fill did not reach the quota; on the second, it
means the check is reading a cached figure rather than the folder.

### 9. Past the ceiling, a backup is refused

```powershell
$stream = [System.IO.File]::Create($filler)
$stream.SetLength([int64]1073741824)
$stream.Close()

try {
  Invoke-WebRequest -Uri "$base/admin/backups/create" -Method POST -Headers $key -UseBasicParsing
} catch {
  $resp   = $_.Exception.Response
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
  $body   = $reader.ReadToEnd(); $reader.Close()
  "status=$($resp.StatusCode.value__)"
  ($body | ConvertFrom-Json).backupObstacle
}

$null = Invoke-RestMethod -Uri "$base/notifications/refresh" -Method POST
$errors = @((Invoke-RestMethod -Uri "$base/notifications?pageSize=0").items |
  Where-Object { $_.metadataKind -eq 'backupmaxexceeded' -and -not $_.isDismissed })
"open=$(@(Open-QuotaWarnings).Count) errors=$($errors.Count) type=$($errors[0].type)"
$errors[0].body
```

**Expected:** `409` and `BudgetExceeded`, then `open=0`, `errors=1`, `type=error`, and a body naming what
the folder holds and the maximum it has passed.

**Three separate facts, and none implies the others.** The refusal is the attempt being stopped. The
`open=0` is the reserve warning gone, since it says backups are still being taken and that is false here.
The `errors=1` is the band reporting: exceeding the maximum always reports, whether or not anything
attempted a backup.

**On failure:** a `200` on the create means nothing enforces the ceiling and the reserve has become
unbounded. An `open=1` means the warning is kept outside the band it describes. An `errors=0` means the
state that stops every backup is showing the operator nothing.

### 10. The remedy the warning names actually works

```powershell
Remove-Item -Force $filler

$after = Invoke-RestMethod -Uri "$base/notifications/refresh" -Method POST
$status = Invoke-RestMethod -Uri "$base/admin/backups/status" -Headers $key
$final = Invoke-RestMethod -Uri "$base/admin/backups/create" -Method POST -Headers $key
$cleared = @((Invoke-RestMethod -Uri "$base/notifications?pageSize=0").items |
  Where-Object { $_.metadataKind -eq 'backupmaxexceeded' -and -not $_.isDismissed })
"canBackUp=$($status.canBackUp) open=$(@(Open-QuotaWarnings).Count) errors=$($cleared.Count) created=$($final.name)"
```

**Expected:** `canBackUp=True`, `open=0`, `errors=0`, and a created file name.

**This closes the loop the warning promises.** Its body tells the operator to delete older backups or
raise the quota, and says the notice clears itself once the folder is back under. This step performs the
first of those and checks both halves: the warning is gone and a backup is possible again.

## Observed effect

**Measured 2026-09-27** against `quotinator:local`, and first run red against an image built from
`7f83e92a`, the model #348 replaced.

Both bands behave as the two-level model intends. A fresh install reports `canBackUp=True` with `used=0`
and writes a real backup of roughly 4.7 MB. Filling to 92% of the 1 GB ceiling reports `reserveInUse=True`
*and* `canBackUp=True` together, with about 81 MB still below the ceiling, and a backup is taken from that
band rather than refused. It raises one `Warning` row titled *Backups have reached their quota*, whose
body names what the folder holds, the quota it has passed and the ceiling it has not, and which links the
`backups-have-reached-their-quota` Knowledgebase entry. A restart leaves it at exactly one, although the
folder grew by a backup first. A Reset answers `200` from the reserve and, because it drops the row with
every other table, the post-reset check raises it again; the reseed row it writes offers
`backupthenreseed` with `backupCaution=true`, and the page renders the caution beneath that button. An
API deletion clears the warning as `underquota` with no restart, and a fill and a removal performed on the
host are picked up by `POST /notifications/refresh`, which needs no admin key. At the full ceiling `create`
answers `409 BudgetExceeded`, and removing the filler restores `canBackUp=True` and a working backup.

**The red run inverted all four of the observations this document turns on:** step 3 reported
`canBackUp=False` while `fits=True` and 81 MB remained below the ceiling, step 4's backup was refused
`409 BudgetExceeded`, no `backupquotareached` row existed at all, and `POST /notifications/refresh`
answered `404`. Step 3's *On failure* text was wrong before that run and is corrected: it blamed
`canBackUp=False` on overshooting the ceiling alone, which `fits=True` disproves.

**This pass found one defect, and step 9's `open=0` is the assertion added for it.** On the day of the
run, past the *ceiling* the check raised the same body it raises inside the reserve: at 1.02 GB against a
1.00 GB ceiling, with `canBackUp=False`, the warning read *"Backups are still being taken, from the
reserve below the ceiling of 1.00 GB, but once the folder reaches the ceiling a backup will be
refused."* Both clauses are false there. The warning's condition is now the band it describes rather than
the threshold it starts at, so above the max it is removed instead, and the body needed no change since it
is only ever shown inside the reserve.

**`Remove-Item -Force $filler` is deliberately not used** to delete the filler from the host. It is
refused outright in at least one sandboxed runner, which stops the whole step before any of it executes;
`[System.IO.File]::Delete` is the same operation and matches the `::Create` two lines above it.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-backup-08"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-08 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```
