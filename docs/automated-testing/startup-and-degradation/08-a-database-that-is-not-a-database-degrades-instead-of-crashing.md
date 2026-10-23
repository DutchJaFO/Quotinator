# A database that is not a database degrades instead of crashing

**Smoke:** yes
**Environment:** Fresh + Constrained
**Deterministic:** yes
**Traces to:** #327

## Preconditions

**Beyond the profile.** A fresh container is seeded, stopped, and its quote database replaced with
bytes that are not a SQLite database at all. The changelog database and `keys/` are left alone: the
fault under test is the quote database, and damaging more would make it ambiguous which one the
degraded state is about.

The container binds its data directory to a host path (`--bind`) rather than using a named volume, so
the file can be replaced from the host.

**The state this establishes, measured 2026-10-21.** SQLite is handed a file it cannot recognise, so
the initializer fails before any schema version is read. The application records the failure and
continues: Kestrel binds, `/health` answers `503`, and the Blazor pages render their degraded form.

## Determinism

- **The file is replaced, not truncated.** Truncation reaches SQLite through a different code path and
  is [`09`](09-a-truncated-database-degrades-instead-of-crashing.md)'s subject. Folding them together
  would let one pass on the other's behalf.
- **The WAL sidecars are deleted along with it.** A surviving `-wal` beside a garbage main file leaves
  SQLite with two disagreeing sources, and which one it reports first is timing rather than a property
  of this test.
- **The container is stopped before the file is touched.** Writing under a running container races its
  own connections, so the outcome would depend on when the write landed.
- **Wait for `unhealthy`, not for a `503`.** Degrading is the expected outcome, so waiting for `200`
  would spend the whole timeout, and the startup wait page answers `503` with `status=starting`, so
  waiting on the status code can return before the degraded state is settled. Step 3 polls the `status`
  field, the technique [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.
- **A regex holding a backslash goes in its own variable, never inside an interpolated string.** In
  Windows PowerShell 5.1 a pattern such as `at [A-Za-z.]+\(` written directly inside `"$( ... )"` is a
  parse error on the escaped parenthesis, not a failed match. Measured 2026-10-21, and step 4 writes it
  the working way.

## Explicitly not covered here

- **The per-element DOM reads and the browser console.** The degraded page is the same page whatever
  fault produced it, and
  [`05`](05-degraded-pages-survive-a-migration-failure.md) asserts its structure element by element
  and requires every console error to be a `503`. This document asserts that the same contract holds
  for an unreadable database rather than repeating that depth: status, the degraded text, and the
  absence of a stack trace.
- **Which backup obstacle the refusal names, and the remedies it offers.** That is
  [`backup/01`](../backup/01-refuses-a-reset-when-the-source-cannot-be-read.md)'s subject, which
  provokes this same state to assert the Reset path. This document asserts only that the recovery
  surface is reachable and that the route it offers resolves the fault, which is the startup contract
  rather than the backup contract.

## Steps

### 1. Seed a healthy database

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-08"
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 `
  --image quotinator:local --bind $dataDir

(Invoke-RestMethod "http://localhost:18408/api/v1/version").database.quotes
```

**Expected:** a non-zero quote count, and `quotinatordata.db` present in `$dataDir`.

**On failure:** a fresh seed that cannot reach healthy is not this document's subject, and every
assertion below would be about the wrong fault. Stop and fix that first.

### 2. Stop the container and replace the database with non-database bytes

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-08"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind $dataDir

Set-Content -Path "$dataDir\quotinatordata.db" -Value "this file is not a SQLite database" -NoNewline
Remove-Item "$dataDir\quotinatordata.db-wal","$dataDir\quotinatordata.db-shm" -ErrorAction SilentlyContinue
(Get-Item "$dataDir\quotinatordata.db").Length
```

**Expected:** the file is 34 bytes.

**On failure:** a file still megabytes in size means the write did not land, and every step below would
assert against a healthy database. Stop rather than continuing.

### 3. Start the current build, and confirm it degraded rather than died

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-08"
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 `
  --image quotinator:local --bind $dataDir --wait-listening

# A 503's body is read from the response stream: PowerShell 5.1 leaves ErrorDetails.Message empty here.
function Health {
  try   { Invoke-RestMethod "http://localhost:18408/api/v1/health" }
  catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { return [pscustomobject]@{ status = 'down' } }
    $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    try { $body | ConvertFrom-Json -ErrorAction Stop } catch { [pscustomobject]@{ status = 'unreadable' } }
  }
}
$deadline = (Get-Date).AddSeconds(180)
while (($health = Health).status -notin 'unhealthy', 'healthy' -and (Get-Date) -lt $deadline) { Start-Sleep 1 }

docker inspect -f '{{.State.Running}}' qt-startup-08
"status=$($health.status)"
"reasonNamesAWorkableRemedy=$($health.reason -match 'deleting the database file')"
```

**Expected:** `true` from `docker inspect`, `status=unhealthy`, and `reasonNamesAWorkableRemedy=True`.

**`true` and `unhealthy` together are the point of this document.** `true` is the never-crash contract:
the process is still running after a failure that reached it before any schema version was read.
`unhealthy` is the confirmation that the failure state was actually reached, rather than inferred from
the recipe having been followed.

**The reason has to name a remedy that can work.** This fault gets the generic initialisation-failure
reason, which names two: a database Reset, which refuses here (step 6), and deleting the database file
and restarting, which works (step 7). The second is the one that has to be there.

**On failure:** a container that is not running means the fault killed the process, which is the defect
this document exists to catch. `status=healthy` means the file was not actually replaced, so re-run from
step 2 rather than letting the assertions below pass against a container that never degraded. `starting`
or `down` means the wait gave up after 180 seconds.

### 4. Read the degraded pages and the OpenAPI surface

```powershell
$stackTrace = 'at [A-Za-z.]+\('

foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18408$path" --expect 200 --status
}
dotnet script scripts/testing/http.csx -- --url "http://localhost:18408/openapi/v1.json" --expect 200 --status

$body = (Invoke-WebRequest "http://localhost:18408/" -UseBasicParsing).Content
$degraded = $body -match 'started with a problem'
$crashed  = $body -match $stackTrace
"degradedTextPresent=$degraded"
"stackTracePresent=$crashed"
```

**Expected:** `200` from all three pages and from `/openapi/v1.json`, with
`degradedTextPresent=True` and `stackTracePresent=False`.

**`/openapi/v1.json` is the recovery route, not a nice-to-have.** The degraded state's whole contract
is that an operator can still read what happened and reach the documented way out, and the spec is how
that way out is discovered. A degraded container that stopped serving its own spec would leave the
operator with a broken UI and no documented next step.

**On failure:** a `500` from any page is the degraded UI failing to render, which is the historical
incident this contract exists to prevent. `stackTracePresent=True` means the page rendered an exception
instead of the degraded form, which is the same failure wearing a `200`.

### 5. Confirm every endpoint that queries the database is gated

```powershell
foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/stats', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18408$path" --expect 503 --status
}
```

**Expected:** `503` from every one of the seven.

**With health reporting a problem, no endpoint that reads the database is expected to return sane
results, so each one is gated rather than left to answer from a database it could not open**
(developer direction, 2026-10-21). This is the complement of step 4: the pages stay reachable because
they render a degraded form that says so, while the data-serving API refuses outright. A `200` from
any of these would be worse than a `503`, because the caller cannot tell an empty database from one
that failed to open.

**On failure:** a `200` with a body means an endpoint answered from a database the initializer could
not read, and whatever it returned is not a result. A `500` means it reached the database and threw
rather than being gated, which is the same defect wearing a different code.

### 6. Confirm the recovery route is reachable, and read what it offers

```powershell
$reset = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18408/api/v1/admin/database/reset" --method POST --expect 409 | ConvertFrom-Json
"reachedTheHandler=$($null -ne $reset.backupObstacle)"
"namesAFileRemedy=$([bool](@($reset.remedies) -match 'file'))"
```

**Expected:** `409`, with `reachedTheHandler=True` and `namesAFileRemedy=True`.

**Reachable and workable are different claims, and this step separates them.** Step 5 gates every
read; this one establishes that the recovery surface is the exception, and it has to be. The `409` establishes
that the admin request reached its handler rather than being answered by the health gate, which is what
makes the surface a recovery route at all. What it does *not* establish is that the route succeeds:
this reset refuses, because a file SQLite cannot open cannot be backed up and cannot be dropped table
by table either. `namesAFileRemedy` is the half that matters here, that the refusal hands the operator
a remedy pointing at the file itself, which step 7 then proves works.

**On failure:** a `503` would mean the health gate answered instead of the handler, so the recovery
surface is not actually reachable while degraded. A `500` would mean the refusal is not reached at all.

### 7. Apply the remedy, and confirm the container recovers

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-08"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind $dataDir

Remove-Item "$dataDir\quotinatordata.db"

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-08 --port 18408 `
  --image quotinator:local --bind $dataDir

dotnet script scripts/testing/http.csx -- --url "http://localhost:18408/api/v1/health" --expect 200 --status
```

**Expected:** the container reaches healthy and `/health` returns `200`.

**This is the positive control and the remedy proof in one step.** Every assertion above is about a
degraded container, so without a passing case this document would stay green against a build that
degraded on everything. It is also the proof that the remedy step 3 read out of the reason and step 6
read out of the refusal, removing the database file from outside the application, actually resolves the
condition rather than being advice nobody checked.

**On failure:** a `503` here means either the degradation is not specific to the sabotage, which makes
every assertion above worthless, or the named remedy does not work, which is a defect in the guidance
rather than in this document.

## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`, seeded to 795 quotes and then
replaced with 34 bytes of text.

The process stays up and `/health` settles at `503 unhealthy`, its reason naming the file-deletion
remedy. `/`, `/stats` and `/notifications` each return `200`,
`/openapi/v1.json` returns `200`, the home page carries *started with a problem*, and no page matches
`at [A-Za-z.]+\(`. Every database-reading endpoint is gated: `/api/v1/quotes`,
`/api/v1/quotes/random`, `/api/v1/quotes/search`, `/api/v1/stats`, `/api/v1/notifications`,
`/api/v1/masterdata/sources` and `/api/v1/version` all answer `503`.

**`GET /api/v1/version` reached that list by being a defect this document found.** On the build this
document was first written against it answered `200` with `"status":"ready"` and a `database` block
reporting every count as `0`, so a caller could neither tell an empty database from an unopenable one
nor see that the application was not ready. [`03`](03-startup-wait-page.md) records the rule that
broke, from #419: the endpoint is gated *while starting* precisely because "a caller could otherwise
reach for it as a readiness endpoint in `/health`'s place", and during startup it does answer `503`.
The gate existed and the degraded state was not behind it. It now answers
`503 {"status":"degraded","version":...,"environment":...}` with no `database` block, and it still
answers `200` when healthy.

**Six admin endpoints answer an unhandled `500` in this state, and this document does not assert
them.** Measured 2026-10-21, each returning a bare *"An error occurred while processing your request"*
and a trace id: `GET /api/v1/admin/database/seed/preview`, `GET /api/v1/admin/audit`,
`GET /api/v1/admin/audit/date-range`, `GET /api/v1/admin/audit/export`,
`POST /api/v1/admin/database/reseed` and `DELETE /api/v1/admin/audit`. The admin surface is exempt
from the health gate so it can be the recovery route, which is why step 6 asserts the reset reaches its
handler; these six reach theirs and throw, and two of them are writes.

Correct in the same run, which is what shows the fault is specific rather than the surface being
broken: `POST /api/v1/admin/backups/create` answers `409`, and `GET /api/v1/admin/backups` and
`/backups/status` answer `200` off the filesystem. `POST /api/v1/admin/sources/refresh` answers `200`,
having really fetched from GitHub, since it writes files and never reads the database.

That is the same defect class #348 removed from `POST /admin/database/reset`, which returned an
unhandled `500` for its own reason until it was taught to refuse with a stated obstacle, and the
endpoints carrying #348's preflight are exactly the ones answering correctly.
[#453](https://github.com/DutchJaFO/Quotinator/issues/453) owns the fix: narrowing the gate's exemption
to the reset and the backup category, which is the repair route an operator can actually use. ADR 022
governs it, since a condition the code has already detected is being thrown rather than returned.

**Recovery route, and whether it can succeed.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and answers `409 Conflict` rather than being gated away by the `503`. **Not workable as
offered**: the reset refuses, naming the obstacle and handing back a remedy that points at the file.
Deleting the database file and restarting returns the container to `200`, measured in step 7.

**The reason names a remedy that works, second.** `/health` carries the generic initialisation-failure
reason, which recommends a database Reset first, and that reset refuses. Its second remedy, *stopping
the app, deleting the database file, and restarting*, is the one that works, and step 3 asserts it is
there. The reason's diagnosis does not fit this fault, though: it says the recorded schema version
probably does not match the on-disk schema, for a file that is not a database at all. Contrast
[`05`](05-degraded-pages-survive-a-migration-failure.md), where #326 gave the unwritable-directory fault
its own reason, matched on SQLite error codes 14 and 8; an unreadable file raises neither, so it falls
through to the generic text.

## Canary

**A pre-issue build.** The commit before #327's first commit, under a distinct tag, run through every
step. That build predates the gating of `/version` while degraded and #348's reset refusal, so step 5
reads `200` from `/api/v1/version` and step 6 reads an unhandled `500` from the reset.

**The positive control this run produces for itself.** Step 7 removes the sabotage and reads `200` in
the same environment, so a build that degraded unconditionally fails there. Step 3's `unhealthy` is the
other half: it ends the run when the sabotage did not land, so the assertions in steps 4 to 6 cannot
pass against a healthy container.

**Where the canary has been run:** 2026-10-23 against `e4078444`, the commit before #327's first, as
`quotinator:canary-327`. Steps 1 to 4 passed; step 5 read `200` from `/api/v1/version`, and step 6 read
`500` from the reset. Step 7 read `healthy`.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-08"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
