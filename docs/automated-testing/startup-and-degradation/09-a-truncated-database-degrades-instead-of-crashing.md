# A truncated database degrades instead of crashing

**Smoke:** no
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327

## Preconditions

**Beyond the profile.** A fresh container is seeded, stopped, and its quote database cut to half its
length: a genuine SQLite file with a valid header and missing pages, which is what an interrupted write
or a filled disk actually leaves behind. The changelog database and `keys/` are left alone.

The container binds its data directory to a host path (`--bind`) rather than using a named volume, so
the file can be truncated from the host.

**Why this is its own document rather than a second case inside
[`08`](08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md).** A truncated database
and a file that is not a database are different faults reaching SQLite through different code paths,
and each has a variable its own `Determinism` has to pin. Folding them together would let one pass on
the other's behalf. The same split exists one category over, between
[`backup/01`](../backup/01-refuses-a-reset-when-the-source-cannot-be-read.md) and
[`backup/02`](../backup/02-refuses-a-reset-when-the-database-is-truncated.md), for the same reason.

## Determinism

- **Half the file, not a fixed byte count.** The bundled dataset's size moves between releases, so a
  literal length would silently stop truncating anything once the database grew past it.
- **The WAL sidecars are deleted.** A surviving `-wal` can carry enough recent pages for SQLite to open
  the database anyway, which would make the outcome depend on how recently the seed checkpointed.
- **The container is stopped before the file is touched.** `destroy` is `docker rm -f`, a kill rather
  than a clean stop, so the sidecars can survive it, which is why the bullet above deletes them rather
  than relying on a checkpoint.
- **The second start waits for *listening*, not healthy**, since degrading is the expected outcome and
  waiting for `200` would spend the whole timeout and then fail for the wrong reason.
- **A regex holding a backslash goes in its own variable, never inside an interpolated string.** In
  Windows PowerShell 5.1 a pattern such as `at [A-Za-z.]+\(` written directly inside `"$( ... )"` is a
  parse error on the escaped parenthesis, not a failed match. Measured 2026-10-21.

## Explicitly not covered here

- **The per-element DOM reads and the browser console.** The degraded page is the same page whatever
  fault produced it, and [`05`](05-degraded-pages-survive-a-migration-failure.md) asserts its structure
  element by element and requires every console error to be a `503`. This document asserts that the
  same contract holds for a truncated database rather than repeating that depth.
- **Which backup obstacle the refusal names, and the remedies it offers.** That is
  [`backup/02`](../backup/02-refuses-a-reset-when-the-database-is-truncated.md)'s subject, which
  provokes this same state to assert the Reset path. This document asserts only that the recovery
  surface is reachable and that the route it offers resolves the fault.

## Steps

### 1. Seed a healthy database and record its size

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-09"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-09 --port 18409 `
  --image quotinator:local --bind $dataDir

(Invoke-RestMethod "http://localhost:18409/api/v1/version").database.quotes
```

**Expected:** a non-zero quote count, and `quotinatordata.db` present in `$dataDir`.

**On failure:** a fresh seed that cannot reach healthy is not this document's subject. Stop and fix
that first.

### 2. Stop the container and cut the database in half

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-09"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind $dataDir

$path = "$dataDir\quotinatordata.db"
$len  = (Get-Item $path).Length
$fs   = [System.IO.File]::Open($path, 'Open', 'Write')
$fs.SetLength([int64]($len / 2))
$fs.Close()
Remove-Item "$dataDir\quotinatordata.db-wal","$dataDir\quotinatordata.db-shm" -ErrorAction SilentlyContinue
"truncated $len -> $((Get-Item $path).Length)"
```

**Expected:** the reported new length is half the old one, and both are megabytes rather than bytes.

**On failure:** a file-in-use error means the container did not stop. Re-run `destroy` and retry rather
than forcing the write.

### 3. Start the current build, and confirm it degraded rather than died

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-09"
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-09 --port 18409 `
  --image quotinator:local --bind $dataDir --wait-listening

docker inspect -f '{{.State.Running}}' qt-startup-09
dotnet script scripts/testing/http.csx -- --url "http://localhost:18409/api/v1/health" --expect 503 --status
```

**Expected:** `true` from `docker inspect`, then `503` from health.

**Both halves are the point of this document.** `true` is the never-crash contract. `503` is the
confirmation that the failure state was actually reached, rather than inferred from the recipe having
been followed.

**On failure:** a container that is not running means the fault killed the process, which is the defect
this document exists to catch. A `200` means the truncation did not take, so re-run from step 2.

### 4. Read the degraded pages and the OpenAPI surface

```powershell
$stackTrace = 'at [A-Za-z.]+\('

foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18409$path" --expect 200 --status
}
dotnet script scripts/testing/http.csx -- --url "http://localhost:18409/openapi/v1.json" --expect 200 --status

$body = (Invoke-WebRequest "http://localhost:18409/" -UseBasicParsing).Content
$degraded = $body -match 'started with a problem'
$crashed  = $body -match $stackTrace
"degradedTextPresent=$degraded"
"stackTracePresent=$crashed"
```

**Expected:** `200` from all three pages and from `/openapi/v1.json`, with `degradedTextPresent=True`
and `stackTracePresent=False`.

**`/openapi/v1.json` is the recovery route, not a nice-to-have.** The degraded state's contract is that
an operator can still read what happened and reach the documented way out, and the spec is how that way
out is discovered.

**On failure:** a `500` from any page is the degraded UI failing to render.
`stackTracePresent=True` means the page rendered an exception instead of the degraded form, which is
the same failure wearing a `200`.

### 5. Confirm every endpoint that queries the database is gated

```powershell
foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/stats', '/api/v1/notifications', '/api/v1/masterdata/sources') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18409$path" --expect 503 --status
}
```

**Expected:** `503` from every one of the six.

**With health reporting a problem, no endpoint that reads the database is expected to return sane
results** (developer direction, 2026-10-21). A truncated database is the sharper case for this rule
than `08`'s: the header is valid and some pages are present, so an endpoint that answered anyway could
return *partial real data*, which is harder to recognise as wrong than an empty result.

**On failure:** a `200` with a body means an endpoint answered from a half-written file, and whatever it
returned is not a result. A `500` means it reached the database and threw rather than being gated.

### 6. Confirm the recovery route is reachable, and read what it offers

```powershell
$reset = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18409/api/v1/admin/database/reset" --method POST --expect 409 | ConvertFrom-Json
"reachedTheHandler=$($null -ne $reset.backupObstacle)"
"namesAFileRemedy=$([bool](@($reset.remedies) -match 'file'))"
```

**Expected:** `409`, with `reachedTheHandler=True` and `namesAFileRemedy=True`.

**Reachable and workable are different claims.** Step 5 gates every read; this one establishes that the
recovery surface is the exception, and it has to be. The `409` establishes that the admin request
reached its handler rather than being answered by the health gate. It does not establish that the route
succeeds: this reset refuses, because a file SQLite cannot read cannot be backed up.
`namesAFileRemedy` is the half that matters, and step 7 proves that remedy works.

**On failure:** a `503` would mean the health gate answered instead of the handler, so the recovery
surface is not reachable while degraded. A `500` would mean the refusal is not reached on this path
even though it is on `08`'s.

### 7. Apply the remedy, and confirm the container recovers

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-09"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind $dataDir

Remove-Item "$dataDir\quotinatordata.db"

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-09 --port 18409 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18409/api/v1/health" --expect 200 --status
```

**Expected:** the container reaches healthy and `/health` returns `200`.

**This is the positive control and the remedy proof in one step.** Every assertion above is about a
degraded container, so without a passing case this document would stay green against a build that
degraded on everything. It is also the proof that replacing the database file from outside the
application resolves the condition.

**On failure:** a `503` here means either the degradation is not specific to the truncation, which
makes every assertion above worthless, or the named remedy does not work, which is a defect in the
guidance rather than in this document.

## Observed effect

**Measured 2026-10-21** against a freshly built `quotinator:local`, seeded to 795 quotes and truncated
from 4,689,920 to 2,344,960 bytes.

The process stays up and `/health` reports `503`. `/`, `/stats` and `/notifications` each return `200`,
`/openapi/v1.json` returns `200`, the home page carries *started with a problem*, and no page matches
`at [A-Za-z.]+\(`. Every data-serving endpoint answers `503`.

**The API surface is indistinguishable from `08`'s**, which is why each document pins its own variable
rather than one standing in for both: the faults differ at the SQLite boundary, and only the recipe
proves which one was provoked.

**`GET /api/v1/version` is not gated here either**, answering `200` with `"status":"ready"` and every
`database` count at `0`. The finding is recorded once, in
[`08`](08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md)'s Observed effect, with
#419's rule and the startup-state control that shows the gate exists. This document asserts nothing
about it, in either direction, for the same reason.

**Recovery route, and whether it can succeed.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and answers `409 Conflict`. **Not workable as offered**: the reset refuses, handing back a
remedy that points at the file. Deleting the database file and restarting returns the container to
`200`, measured in step 7.

## Canary

**The positive control this run produces for itself**, since the never-crash contract predates every
commit this document traces to and no pre-fix build applies. Step 7 removes the truncation and reads
`200` in the same environment, so a build that degraded unconditionally fails there. Step 3's
`--expect 503` is the other half: it ends the run when the truncation did not take, so the assertions
in steps 4 to 6 cannot pass against a healthy container.

**Where the canary has been run:** step 7 returned `200` on 2026-10-21 against `quotinator:local` at
commit `15f5b589`, in the same run that produced the readings above.

Deliberately not this document's own pass or fail: step 3 asserting `503` proves the truncation landed,
not that the contract holds.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-09"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-09 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
