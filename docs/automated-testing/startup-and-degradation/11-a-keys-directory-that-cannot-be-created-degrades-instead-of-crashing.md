# A keys directory that cannot be created degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327, #326
**Fully green after:** #332: until the DataProtection keys directory has a fallback location, step 1 reads `reasonNamesTheKeysDirectory=False`, step 3 reads `stillUnhealthyAfterReset=False`, and step 4 reads `500` from all three pages. Steps 2 and 5 pass, and every step still runs. #332's requirement 10 supersedes this scenario's degraded handling, so #332 also rewrites this document to whatever its fallback chain makes of the same sabotage.

## Preconditions

**Beyond the profile.** `/data` is writable, and a plain file sits where the `keys/` directory belongs, so
`Directory.CreateDirectory` for the DataProtection key ring throws at configuration time, before Kestrel
binds. Everything else in the data directory can be written: the database is created and seeded
normally. That is what separates this scenario from
[`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md), where nothing at
all can be written.

The container binds its data directory to a host path (`--bind`), so the file can be placed and removed
from the host, and uses `--own-keys`, so `test-env.csx` mounts no shared key ring over the file.

**The state this establishes, measured 2026-10-23.** The configuration-time guard records the failure as
the data-directory reason, startup continues, the database is created and seeded, and `/health` settles at
`503` once initialisation finishes.

## Determinism

- **A file at the `keys/` path, rather than a permission.** It makes `CreateDirectory` throw identically
  on every platform, which an ACL or a read-only sub-mount does not: a `keys/` directory that already
  exists read-only lets `CreateDirectory` succeed and moves the failure to the first key write.
- **`--own-keys` is required, not a preference.** Without it `test-env.csx` mounts the shared key ring at
  `/data/keys`, over the file, and the sabotage never reaches the application.
- **Wait for `unhealthy`, not for a `503`.** This container seeds a fresh database before it settles, and
  the startup wait page answers `503` with `status=starting` the whole time. A wait on the status code
  returns at once and every assertion after it reads a half-started application. Step 1 polls the
  `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established for the same reason.
- **The keys sabotage is checked before the database.** The configuration-time failure marks the
  application unhealthy before initialisation runs, and the first failure recorded wins, so a healthy
  database cannot overwrite the degraded state, and the reason read in step 1 is the keys failure's.
- **A regex holding a backslash goes in its own variable**, never inside an interpolated string: in
  Windows PowerShell 5.1 a pattern such as `at [A-Za-z.]+\(` inside `"$( ... )"` is a parse error.

## Explicitly not covered here

- **The whole data directory unwritable.** That is
  [`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md).
- **Where the key ring lands once it has a fallback location.** That is #332's requirement 10, and its
  own tests.

## Steps

### 1. Block `keys/`, start, and confirm it degraded rather than died

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-11"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
Set-Content -Path "$dataDir\keys" -Value "not a directory" -NoNewline -Encoding ascii

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-11 --port 18411 `
  --image quotinator:local --bind $dataDir --own-keys --wait-listening

# A 503's body is read from the response stream: PowerShell 5.1 leaves ErrorDetails.Message empty here.
function Health {
  try   { Invoke-RestMethod "http://localhost:18411/api/v1/health" }
  catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { return [pscustomobject]@{ status = 'down' } }
    $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    try { $body | ConvertFrom-Json -ErrorAction Stop } catch { [pscustomobject]@{ status = 'unreadable' } }
  }
}
$deadline = (Get-Date).AddSeconds(180)
while (($health = Health).status -notin 'unhealthy', 'healthy' -and (Get-Date) -lt $deadline) { Start-Sleep 1 }

docker inspect -f '{{.State.Running}}' qt-startup-11
"status=$($health.status)"
"reasonNamesTheKeysDirectory=$($health.reason -match 'keys')"
"keysIsStillAFile=$(-not (Get-Item "$dataDir\keys").PSIsContainer)"
```

**Expected:** `true` from `docker inspect`, `status=unhealthy`, `reasonNamesTheKeysDirectory=True` and
`keysIsStillAFile=True`.

**`true` and `unhealthy` together are the never-crash contract**, for a failure that happened before
Kestrel bound. `keysIsStillAFile` confirms the sabotage held rather than being replaced by a directory.

**The reason has to point at what is actually wrong.** The data directory here is writable: the database
was created and seeded. A reason telling the operator the data directory cannot be written, and that a
Reset cannot help, sends them to fix something that is not broken and away from a Reset that in fact
runs (step 3). Naming the keys directory is what makes the reason's remedy one that can work.

**On failure:** a container that is not running means the configuration-time failure killed the process,
which is the defect this document exists to catch. `status=healthy` means the file did not block
anything, so every step below asserts against a healthy container: stop. `starting` or `down` means the
wait gave up after 180 seconds. `reasonNamesTheKeysDirectory=False` is the header's #332 reading; carry on.

### 2. Confirm the OpenAPI surface answers and every database read is gated

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18411/openapi/v1.json" --expect 200 --status

foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/stats', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18411$path" --expect 503 --status
}
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**The database is healthy here, and the reads are gated anyway.** Health is one state: once startup marks
it failed, every database read refuses, whichever fault did it. That is the gate working as designed,
and it is why step 1's reason matters so much: it is the only place the operator learns which fault it
was.

**On failure:** a `200` from any of the seven means the gate is not consulting the degraded state, which
is the gating rule failing for a database that happens to work.

### 3. Confirm the recovery route is reachable, and whether it clears the fault

```powershell
dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18411/api/v1/admin/database/reset" --method POST --expect 200 --status

$after = Health
"stillUnhealthyAfterReset=$($after.status -eq 'unhealthy')"
"keysIsStillAFile=$(-not (Get-Item "$dataDir\keys").PSIsContainer)"
```

**Expected:** `200` from the reset, `stillUnhealthyAfterReset=True` and `keysIsStillAFile=True`.

**Reachable, and it runs, but it cannot be the remedy.** The data directory is writable, so a Reset
succeeds: it backs up, rebuilds the schema and answers `200`. What it cannot do is create `keys/`, so the
fault that degraded the application is untouched. `/health` must therefore still report it. A health
check that turns green because an unrelated repair succeeded hides a live fault from monitoring at the
exact moment an operator believes it fixed.

**On failure:** a `503` or `409` from the reset means the recovery surface is not reachable or refuses for
a reason this scenario does not create. `stillUnhealthyAfterReset=False` is the header's #332 reading;
carry on.

### 4. Read the degraded pages

```powershell
$stackTrace = 'at [A-Za-z.]+\('

foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18411$path" --expect 200 --status
}

$body = (Invoke-WebRequest "http://localhost:18411/" -UseBasicParsing).Content
"degradedTextPresent=$($body -match 'started with a problem')"
"stackTracePresent=$($body -match $stackTrace)"
```

**Expected:** `200` from all three pages, with `degradedTextPresent=True` and `stackTracePresent=False`.

**On failure:** a `500` from any page is the degraded UI failing to render, which the never-crash
contract forbids. Until #332, every page fails here reading the DataProtection key ring; step 5 still
runs.

### 5. Apply the remedy, and confirm the container recovers

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-11"
docker stop -t 15 qt-startup-11
Remove-Item "$dataDir\keys" -Force

dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-11 --port 18411 `
  --image quotinator:local --bind $dataDir --own-keys

dotnet script scripts/testing/http.csx -- --url "http://localhost:18411/api/v1/health" --expect 200 --status
foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18411$path" --expect 200 --status
}
"keysIsADirectory=$((Get-Item "$dataDir\keys").PSIsContainer)"
```

**Expected:** the container reaches healthy, `/health` and all three pages answer `200`, and
`keysIsADirectory=True`.

**This is the positive control and the remedy proof in one step.** Without a passing case every
assertion above would stay green against a build that degraded on everything. It also proves the remedy
that actually clears this fault: remove what occupies the `keys/` path and restart, after which the
application creates the directory and the key ring itself.

**On failure:** a `503` here means the degradation was not specific to the blocked `keys/` path, which
makes every assertion above worthless.

## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`.

The process stays up and the database is created and seeded, while `/health` settles at `503` with the
data-directory reason: *"The data directory cannot be written … Restore write access to the data
directory and restart. A database Reset cannot resolve this: it writes too."* None of that describes this
fault: the data directory is writable, and the Reset runs. `/openapi/v1.json` answers `200` and all
seven database reads answer `503`.

**All three pages answer `500`**, failing to read the DataProtection key ring
(`ErrorOccurredWhileReadingKeyRing`, once per page request), as in
[`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md).

**A Reset answers `200` and turns `/health` healthy while the fault remains.** Measured: after the reset,
`/health` `200`, `/api/v1/quotes` `200` with the emptied database, and `/` still `500`, with `keys` still
a file. The Reset path calls `DatabaseHealthState.MarkHealthy`, which clears every recorded failure
rather than only a database one, because #326 records the keys failure in the database's health state.

**Recovery route, and whether it can succeed.** Reachable, and it runs, but it is not a remedy: nothing
the admin surface offers can create `keys/` while a file occupies its path. Removing the file and
restarting returns the container to `200` with every page rendering, measured in step 5.

All three readings that diverge from the contract trace to one decision: #326 folds an uncreatable
`keys/` directory into the database health state with the data-directory reason. #332's requirement 10
replaces that handling with a fallback chain for the keys location.

## Canary

A build from the commit before [#326](https://github.com/DutchJaFO/Quotinator/issues/326)'s first code
change, under a distinct tag, run through step 1. On that build the configuration-time `CreateDirectory`
for `keys/` throws before Kestrel binds: step 1's `docker inspect` reads `false` and nothing answers on
the port.

Step 5 is the positive control inside every run: it removes the sabotage and reads `200`, so a build that
degraded unconditionally fails there.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-11"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-11 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and the bind directory is removed, so the next run starts from
nothing.
