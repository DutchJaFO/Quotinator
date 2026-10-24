# A keys directory that cannot be written degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327

## Preconditions

**Beyond the profile.** `/data` is a writable bind directory, and an empty host directory is mounted
read-only over `/data/keys`. The `keys/` directory therefore exists, so creating it succeeds, but no key
file can be written into it: the failure is in the key ring, not the data directory, and the database is
created and seeded normally.

The container is started with its own `docker run`, since `test-env.csx` has no option for a separate
mount at `keys/`. It carries the profile's settings and nothing else.

**The state this establishes.** A key ring that cannot be written fails on every page request, so the
application has to notice it at startup and report itself degraded, or the operator meets page failures
with nothing telling them why.

## Determinism

- **A read-only mount, not a missing directory.** A `keys/` path that cannot be created is
  [`11`](11-a-keys-directory-that-cannot-be-created-degrades-instead-of-crashing.md)'s subject, and
  fails at a different point: `CreateDirectory` throws there, and succeeds here.
- **The keys mount is an empty directory.** A key ring that already holds keys can be read but not
  rotated, which is a different, later failure.
- **Wait for `unhealthy` or `healthy`, never for a `503`.** The startup wait page answers `503` with
  `status=starting` while the fresh database seeds, so step 1 polls the `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.

## Explicitly not covered here

- **How the degraded pages render.** This document proves the application says it is degraded instead
  of crashing, which is #327's contract. The pages fail on the key ring in this state, as they do for an
  uncreatable `keys/`; that is [#454](https://github.com/DutchJaFO/Quotinator/issues/454)'s, and is
  recorded under *Observed effect*.

## Steps

### 1. Start with `keys/` mounted read-only, and confirm it degraded rather than died

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-13"
$keysDir = "C:\repos\Quotinator\.claude\temp\qt-startup-13-keys"
docker rm -f qt-startup-13 2>$null | Out-Null
Remove-Item -Recurse -Force $dataDir, $keysDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir, $keysDir | Out-Null

docker run -d --name qt-startup-13 -p 18413:8080 -v "${dataDir}:/data" -v "${keysDir}:/data/keys:ro" `
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest `
  -e Quotinator__AutoPurgeBundledImportActions=true -e Quotinator__AutoUpdateSources=false `
  quotinator:local | Out-Null

# A 503's body is read from the response stream: PowerShell 5.1 leaves ErrorDetails.Message empty here.
function Health {
  try   { Invoke-RestMethod "http://localhost:18413/api/v1/health" }
  catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { return [pscustomobject]@{ status = 'down' } }
    $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    try { $body | ConvertFrom-Json -ErrorAction Stop } catch { [pscustomobject]@{ status = 'unreadable' } }
  }
}
$deadline = (Get-Date).AddSeconds(180)
while (($health = Health).status -notin 'unhealthy', 'healthy' -and (Get-Date) -lt $deadline) { Start-Sleep 1 }

docker inspect -f '{{.State.Running}}' qt-startup-13
"status=$($health.status)"
"reasonGiven=$(-not [string]::IsNullOrWhiteSpace($health.reason))"
"keyFilesWritten=$(@(Get-ChildItem $keysDir -Force).Count)"
```

**Expected:** `true` from `docker inspect`, `status=unhealthy`, `reasonGiven=True`, and
`keyFilesWritten=0`.

**`unhealthy` is the whole point of this document.** The key ring cannot be written, so every page
fails; an application reporting `healthy` here has not noticed that it is degraded. `keyFilesWritten=0`
confirms the mount really refused.

**On failure:** a container that is not running means the fault killed the process. `status=healthy`
is the defect this document exists to catch: the application does not know it is degraded. Files in
`$keysDir` mean the mount was writable, so nothing below is about the fault. `starting` or `down` means
the wait gave up after 180 seconds.

### 2. Confirm the OpenAPI surface answers and every database read is gated

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18413/openapi/v1.json" --expect 200 --status

foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/conversations', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18413$path" --expect 503 --status
}
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven.

**The database is healthy here, and the reads are gated anyway**: once startup reports the application
degraded, every database read refuses, whichever fault did it.

**On failure:** a `200` from any of the seven means the degraded state is not gating the reads.

### 3. Remove the sabotage, and confirm the container is healthy

```powershell
docker rm -f qt-startup-13 | Out-Null
docker run -d --name qt-startup-13 -p 18413:8080 -v "${dataDir}:/data" `
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest `
  -e Quotinator__AutoPurgeBundledImportActions=true -e Quotinator__AutoUpdateSources=false `
  quotinator:local | Out-Null

dotnet script scripts/testing/http.csx -- --url "http://localhost:18413/api/v1/health" --wait-for 200 --status
foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/conversations', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18413$path" --expect 200 --status
}
"keyFilesWritten=$(@(Get-ChildItem "$dataDir\keys" -Force -Filter *.xml).Count)"
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. `/health` answers `200`, and `keyFilesWritten` is non-zero.

**This is the positive control.** The same data directory with a writable `keys/` starts healthy and
writes its key ring, so the degraded state above is about the read-only mount and nothing else.

**On failure:** a `503` here means the degradation was not specific to the read-only `keys/`.

## Observed effect

**Measured 2026-10-24** against `quotinator:local` built with #327's step 14 fix.

The process stays up, the database is created and seeded, and `/health` settles at `503 unhealthy` with
the data-directory reason, no key file written. `/openapi/v1.json` answers `200` and all seven database
reads `503`. With `keys/` writable again, the same data directory starts healthy, writes its key ring,
and every one of the seven reads answers `200`.

**The reason names the data directory, not `keys/`.** The data directory here is writable; the reason
is #326's, reused until [#455](https://github.com/DutchJaFO/Quotinator/issues/455) gives `keys/` a reason
of its own. The pages fail on the key ring, as for an uncreatable `keys/`, which is
[#454](https://github.com/DutchJaFO/Quotinator/issues/454)'s. The Knowledgebase entry
`every-page-fails-when-the-keys-directory-cannot-be-created.md` covers what an operator does about it.

## Canary

**A pre-fix build.** Run against the build from before #327's step 14 fix, step 1 reads
`status=healthy`: the application does not notice the key ring cannot be written.

**Where the canary has been run:** 2026-10-24 against `quotinator:local` built from `eb8c1a19`, before
the fix. Step 1 read `status=healthy`, `reasonGiven=False`, `keyFilesWritten=0`; step 2 read `200` from
every database read. Step 3 read `200`.

**The positive control this run produces for itself.** Step 3 removes the sabotage and reads `200`, so a
build that degraded unconditionally fails there.

## Cleanup

```powershell
docker rm -f qt-startup-13 | Out-Null
Remove-Item -Recurse -Force "C:\repos\Quotinator\.claude\temp\qt-startup-13", "C:\repos\Quotinator\.claude\temp\qt-startup-13-keys" -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and both directories are removed, so the next run starts from
nothing.
