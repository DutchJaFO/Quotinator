# A migration refused for want of a backup degrades instead of crashing

**Smoke:** yes
**Environment:** Upgraded + Constrained
**Deterministic:** yes
**Traces to:** #327, #348

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-startup-12`, publishing `18412`, on a bind
directory. The prior image is the published `ghcr.io/dutchjafo/quotinator:1.8.3`, so the current build
has migrations to apply. Before the current build starts, the backup folder is filled to the budget, so
the backup every migration takes first cannot be taken and the migration is refused.

**The state this establishes, measured 2026-10-23.** The initializer refuses the migration, the database
stays at the schema the prior release wrote, and the application degrades with a reason naming the
obstacle and its remedies.

## Determinism

- **The prior version is a published tag**, so pending migrations are guaranteed without editing a version
  row. Editing one does not work as a substitute: forgetting a migration's version row leaves its schema
  change in place, and letting it run again fails on its own DDL (measured in-process by #327, recorded on
  `StartupBackupRefusalTests.Startup_NoMigrationPendingWithAZeroQuota_ReportsHealthy`).
- **The obstacle is one sparse file the size of the whole 1 GB default**, for the reason
  [`backup/06`](../backup/06-a-startup-that-cannot-take-a-backup-loads-nothing.md) records: the backup
  taken before a migration is measured against the absolute ceiling, not the 90% operating quota.
- **Wait for `unhealthy`, not for a `503`.** The startup wait page answers `503` with `status=starting`,
  so step 2 polls the `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.
- **`reenter` stops the previous container cleanly**, both times, so neither restart races its own
  connections.
- **No migration number is asserted.** Only that the remedied start applied at least one migration and
  serves content.

## Explicitly not covered here

- **What the reason says, what the recovery routes offer, and how the pages render.** This document
  proves the application says it is degraded instead of crashing, which is #327's contract; all three
  are recorded under *Observed effect*.
- **That the refused migration leaves the recorded schema version unchanged, and that the degraded page
  links the Knowledgebase entry.** That is
  [`backup/07`](../backup/07-a-migration-that-cannot-take-a-backup-leaves-the-database-unmigrated.md),
  which provokes this same state to assert the backup contract.
## Steps

### 1. Let the prior release create its database, then fill the backup budget

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-12"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$dataDir\backups" | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-12 --port 18412 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir
(Invoke-RestMethod "http://localhost:18412/api/v1/version").database.quotes
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-12 --bind $dataDir

$stream = [System.IO.File]::Create((Join-Path $dataDir "backups\filler.db"))
$stream.SetLength([int64]1073741824)
$stream.Close()
(Get-Item "$dataDir\backups\filler.db").Length
```

**Expected:** a non-zero quote count from the prior release, and a filler of `1073741824` bytes.

**On failure:** a prior release that cannot reach healthy is not this document's subject. A smaller
filler leaves room for the backup, so the migration would run and every step below would assert against
a healthy container.

### 2. Start the current build, and confirm it degraded rather than died

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-12 --port 18412 `
  --image quotinator:local --bind $dataDir --wait-listening

# A 503's body is read from the response stream: PowerShell 5.1 leaves ErrorDetails.Message empty here.
function Health {
  try   { Invoke-RestMethod "http://localhost:18412/api/v1/health" }
  catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { return [pscustomobject]@{ status = 'down' } }
    $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    try { $body | ConvertFrom-Json -ErrorAction Stop } catch { [pscustomobject]@{ status = 'unreadable' } }
  }
}
$deadline = (Get-Date).AddSeconds(180)
while (($health = Health).status -notin 'unhealthy', 'healthy' -and (Get-Date) -lt $deadline) { Start-Sleep 1 }

docker inspect -f '{{.State.Running}}' qt-startup-12
"status=$($health.status)"
"reasonGiven=$(-not [string]::IsNullOrWhiteSpace($health.reason))"
```

**Expected:** `true` from `docker inspect`, `status=unhealthy`, and `reasonGiven=True`.

**`true` and `unhealthy` with a reason are the never-crash contract**, for a startup that refused the
step it came to perform: the application says it is degraded rather than serving from a schema behind
this build.

**On failure:** a container that is not running means the refusal killed the process. `status=healthy`
means the migration ran without its backup, which is #348's defect. `starting` or `down` means the wait
gave up after 180 seconds.

### 3. Confirm the OpenAPI surface answers and every database read is gated

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18412/openapi/v1.json" --expect 200 --status

foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/stats', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18412$path" --expect 503 --status
}
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**The schema is behind this build, so nothing is read from it.** A query the old schema cannot answer
would throw, and one it can answer would be serving through a schema the code does not agree with.

**On failure:** a `200` with a body means an endpoint answered from an unmigrated schema. A `500` means it
reached the database and threw rather than being gated.

### 4. Remove the obstacle, and confirm the database migrates

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-12 --bind $dataDir
Remove-Item (Join-Path $dataDir "backups\filler.db")

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-12 --port 18412 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18412/api/v1/health" --expect 200 --status
"quotes=$((Invoke-RestMethod 'http://localhost:18412/api/v1/version').database.quotes)"
$log = docker logs qt-startup-12 2>&1 | Out-String
"migrationApplied=$(([regex]::Matches($log, 'migration applied')).Count)"
```

**Expected:** `/health` `200`, `quotes` non-zero, and `migrationApplied` non-zero.

**This is the positive control.** Every assertion above is about a degraded container, so without a
passing case this document would stay green against a build that refused every migration. With room for
its backup, the same volume migrates and keeps its content.

**On failure:** a `503` here means the degradation was not specific to the full budget.
## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`, upgrading a `1.8.3` volume
seeded to 799 quotes.

The process stays up and `/health` settles at `503 unhealthy`: *"Database migration refused: no backup
could be taken (BudgetExceeded). The backup folder has reached its storage ceiling, so no new backup can
be written. The database stays at its recorded schema version until a backup can be taken."*, followed
by two remedies, removing old backups through `GET /api/v1/admin/backups` and
`DELETE /api/v1/admin/backups/{name}`, or raising `Quotinator:MaxBackupStorageGb` and restarting, and the
Knowledgebase address. `/openapi/v1.json` answers `200`, all seven database reads `503`, and the three
pages `200`.

**Recovery route, recorded rather than asserted.** A Reset is reachable and refuses with `409`, obstacle
`BudgetExceeded`, for the same reason. The backup category is reachable and is the route that works:
the listing shows `filler.db` at `1073741824` bytes, `DELETE /api/v1/admin/backups/filler.db` answers
`204`, and after a restart the migration applies, one backup is taken, and the container serves 795
quotes.

## Canary

A build from the commit before [#348](https://github.com/DutchJaFO/Quotinator/issues/348)'s refusal, under
a distinct tag, run through step 2. #348 is what made a migration with no backup refuse rather than run
unprotected, so on that build step 2 reads `status=healthy`: the migration runs without its backup.

Step 4 is the positive control inside every run: it removes the obstacle and reads `200`, so a build that
degraded unconditionally fails there.

**Where the canary has been run:** 2026-10-23 against `e4078444`, the commit before #327's first, as
`quotinator:canary-327`, which also predates #348's refusal. Step 2 read `status=healthy` with no
refusal reason: the migration ran without its backup. Step 3 read `200` from every database read.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-12"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-12 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and the bind directory is removed, so the next run starts from
nothing.
