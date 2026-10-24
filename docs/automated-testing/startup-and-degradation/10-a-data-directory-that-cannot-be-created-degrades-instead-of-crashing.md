# A data directory that cannot be created degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327, #326

## Preconditions

**Beyond the profile.** `/data` is mounted read-only (`--read-only-data`) and the data directory is
configured one level beneath it (`Quotinator__DataDir=/data/quotinator`), so the directory does not exist
and cannot be created. This is the earliest degraded path the application has: `Directory.CreateDirectory`
fails at configuration time, before Kestrel binds, and so does the `keys/` directory inside it. Nothing
has ever been written, so there is no database to open either.

The container binds its data directory to a host path (`--bind`), so the host can confirm nothing was
written into it.

**The state this establishes, measured 2026-10-23.** The configuration-time guard records the failure,
Kestrel binds, and the initializer's own attempt to open the database then fails with `SQLite Error 14`.
`/health` answers `503` with the data-directory reason.

## Determinism

- **The parent mount is read-only, rather than a file placed where the directory belongs.** Both make
  `CreateDirectory` throw, and `StartupResilienceTests` uses the file, which is deterministic on every
  platform. This document uses the real-world cause instead, the one an operator actually meets.
- **`create` with `--read-only-data` gives the container its own key ring**, since a brand-new volume
  that cannot be written has none to share (`test-env.csx`'s note on `--read-only-data`). So no `keys/`
  directory exists or can be created, which is what the pages recorded under *Observed effect* fail on.
- **The data directory is configured with `--unset` and then `--env`**, so the profile's own
  `Quotinator__DataDir=/data` is removed rather than relying on the later `-e` winning.
- **Wait for `unhealthy`, not for a `503`.** The startup wait page answers `503` with
  `status=starting` until initialisation finishes, so a wait on the status code can return before the
  degraded state is settled. Step 1 polls the `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established for the same reason.
  Initialisation fails within a second here, since there is nothing to seed, but a wait that is right
  only because it happens to be fast is a guess.
- **No WAL sidecar state to pin.** No database file exists, so there is nothing for SQLite to replay.
## Explicitly not covered here

- **How the degraded pages render, and what the recovery routes offer.** This document proves the
  application says it is degraded instead of crashing, which is #327's contract. Both are recorded under
  *Observed effect*, and `backup/04` provokes an unwritable backup folder to assert the Reset path.
- **A file where the directory belongs.** That sabotage is
  `StartupResilienceTests.Startup_DataDirectoryCannotBeCreated_*`, in-process, where it is
  deterministic across platforms.
## Steps

### 1. Start with the data directory unobtainable, and confirm it degraded rather than died

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-10"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-10 --port 18410 `
  --image quotinator:local --bind $dataDir --read-only-data `
  --unset Quotinator__DataDir --env Quotinator__DataDir=/data/quotinator --wait-listening

# A 503's body is read from the response stream: PowerShell 5.1 leaves ErrorDetails.Message empty here.
function Health {
  try   { Invoke-RestMethod "http://localhost:18410/api/v1/health" }
  catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { return [pscustomobject]@{ status = 'down' } }
    $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    try { $body | ConvertFrom-Json -ErrorAction Stop } catch { [pscustomobject]@{ status = 'unreadable' } }
  }
}
$deadline = (Get-Date).AddSeconds(180)
while (($health = Health).status -notin 'unhealthy', 'healthy' -and (Get-Date) -lt $deadline) { Start-Sleep 1 }

docker inspect -f '{{.State.Running}}' qt-startup-10
"status=$($health.status)"
"reasonGiven=$(-not [string]::IsNullOrWhiteSpace($health.reason))"
"filesWritten=$(@(Get-ChildItem $dataDir -Force).Count)"
```

**Expected:** `true` from `docker inspect`, `status=unhealthy`, `reasonGiven=True`, and `filesWritten=0`.

**`true` and `unhealthy` with a reason are the point of this document.** `true` is the never-crash
contract, for a failure that happened before Kestrel bound. `unhealthy` with a reason is the application
saying it is degraded. `filesWritten=0` confirms the mount really refused: a directory
that was created would mean the sabotage did not land.

**On failure:** a container that is not running means the configuration-time failure killed the process,
which is the defect this document exists to catch. `status=healthy`, or files in `$dataDir`, means
`/data` was writable, so every step below would assert against a healthy container: stop. `starting` or
`down` means the wait gave up after 180 seconds.

### 2. Confirm the OpenAPI surface answers and every database read is gated

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18410/openapi/v1.json" --expect 200 --status

foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/stats', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18410$path" --expect 503 --status
}
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**The spec is how the way out is discovered**, so a degraded container that stopped serving it would
leave the operator with no documented next step. The seven endpoints read the database, which does not
exist, so each refuses rather than answering from nothing.

**On failure:** a `200` with a body from any of the seven means an endpoint answered without a database,
and whatever it returned is not a result. A `500` means it reached the database and threw rather than
being gated.

### 3. Remove the sabotage, and confirm the container is healthy

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-10"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-10 --port 18410 `
  --image quotinator:local --bind $dataDir `
  --unset Quotinator__DataDir --env Quotinator__DataDir=/data/quotinator

dotnet script scripts/testing/http.csx -- --url "http://localhost:18410/api/v1/health" --expect 200 --status
"databaseCreated=$(Test-Path "$dataDir\quotinator\quotinatordata.db")"
```

**Expected:** the container reaches healthy, `/health` answers `200`, and `databaseCreated=True`.

**This is the positive control.** Every assertion above is about a degraded container, so without a
passing case this document would stay green against a build that degraded on everything. The same
configuration on a writable mount creates the directory and the database.

**On failure:** a `503` here means the degradation was not specific to the read-only mount, which makes
every assertion above worthless.
## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`.

The process stays up, `/health` answers `503` with the data-directory reason, and the host directory stays
empty. `/openapi/v1.json` answers `200` and all seven database-reading endpoints answer `503`. The log
carries the configuration-time `[Config]` warning, two `IOException`s for the two directories that could
not be created, and then `SQLite Error 14: 'unable to open database file'` from the initializer.

**All three pages answer `500`, recorded rather than asserted.** With no `keys/` directory, every request
that renders a page fails to read the DataProtection key ring (`ErrorOccurredWhileReadingKeyRing`, five
times for five page requests), while `/health` correctly reports the fault, so the application still says
it is degraded. A finding for its own issue, not this document's contract. The same holds for an uncreatable
`keys/` alone, which [`11`](11-a-keys-directory-that-cannot-be-created-degrades-instead-of-crashing.md)
covers.

**Recovery route, recorded rather than asserted.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and refuses with `409`, obstacle `DestinationDirectoryNotWritable`, naming *restore write
access* first. **Not workable**: a Reset writes. The refusal's second remedy, *Retry with
`allowNoBackup=true`*, answers an unhandled `500` here, since the reset still has to write; this document
does not assert it, so as not to write the `500` in as the contract. Remounting `/data` writable and
restarting returns the container to `200`, which is step 3's positive control.

## Canary

A build from the commit before [#326](https://github.com/DutchJaFO/Quotinator/issues/326)'s first code
change, under a distinct tag, run through step 1. #326 is what turned this failure into a degraded state,
so on that build the configuration-time `CreateDirectory` throws before Kestrel binds: step 1's
`docker inspect` reads `false` and nothing answers on the port.

Step 3 is the positive control inside every run: it removes the sabotage and reads `200`, so a build
that degraded unconditionally fails there.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-10"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-10 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and the bind directory is removed, so the next run starts from
nothing.
