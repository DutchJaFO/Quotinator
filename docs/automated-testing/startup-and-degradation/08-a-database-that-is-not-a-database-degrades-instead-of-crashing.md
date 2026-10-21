# A database that is not a database degrades instead of crashing

**Smoke:** no
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
- **The second start waits for *listening*, not healthy.** Degrading is the expected outcome, so
  waiting for `200` would spend the whole timeout and then fail for the wrong reason.
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

docker inspect -f '{{.State.Running}}' qt-startup-08
dotnet script scripts/testing/http.csx -- --url "http://localhost:18408/api/v1/health" --expect 503 --status
```

**Expected:** `true` from `docker inspect`, then `503` from health.

**Both halves are the point of this document.** `true` is the never-crash contract: the process is
still running after a failure that reached it before any schema version was read. `503` is the
confirmation that the failure state was actually reached, rather than inferred from the recipe having
been followed.

**On failure:** a container that is not running means the fault killed the process, which is the defect
this document exists to catch. A `200` means the file was not actually replaced, so re-run from step 2;
`--expect 503` ends the run here either way rather than letting the assertions below pass against a
container that never degraded.

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
                  '/api/v1/stats', '/api/v1/notifications', '/api/v1/masterdata/sources') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18408$path" --expect 503 --status
}
```

**Expected:** `503` from every one of the six.

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
a remedy pointing at the file itself, which step 6 then proves works.

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
degraded on everything. It is also the proof that the remedy step 6 read out of the refusal, replace
the database file from outside the application, actually resolves the condition rather than being
advice nobody checked.

**On failure:** a `503` here means either the degradation is not specific to the sabotage, which makes
every assertion above worthless, or the named remedy does not work, which is a defect in the guidance
rather than in this document.

## Observed effect

**Measured 2026-10-21** against a freshly built `quotinator:local`, seeded to 795 quotes and then
replaced with 34 bytes of text.

The process stays up and `/health` reports `503`. `/`, `/stats` and `/notifications` each return `200`,
`/openapi/v1.json` returns `200`, the home page carries *started with a problem*, and no page matches
`at [A-Za-z.]+\(`. Every data-serving endpoint is gated: `/api/v1/quotes`, `/api/v1/quotes/random`,
`/api/v1/quotes/search`, `/api/v1/stats`, `/api/v1/notifications` and
`/api/v1/masterdata/sources` all answer `503`.

**`GET /api/v1/version` is the one database-reading endpoint that is not gated, and that is a finding
rather than a property of this scenario.** Measured on both this document's state and
[`09`](09-a-truncated-database-degrades-instead-of-crashing.md)'s: `200`, with `"status":"ready"` and a
`database` block reporting every count as `0`. A caller cannot tell an empty database from one that
could not be opened, and `status: ready` is false while `/health` says otherwise.

[`03`](03-startup-wait-page.md) records the rule this breaks, from #419: `/version` is gated *while
starting* precisely because "a caller could otherwise reach for it as a readiness endpoint in
`/health`'s place". Measured the same day as a control: during startup it answers `503` with the
*"Quotinator is starting up"* problem-details body, no `status`, no `database` block. So the gate
exists and works, and the degraded state is not behind it.

This document deliberately asserts nothing about `/version`, in either direction. Asserting the `200`
would write a defect into the suite as though it were the contract, and asserting a `503` would fail
against the build this document was measured on. Step 5 asserts the six endpoints whose gating holds
now and would still hold after a fix.

**Recovery route, and whether it can succeed.** Reachable: `POST /api/v1/admin/database/reset` reaches
its handler and answers `409 Conflict` rather than being gated away by the `503`. **Not workable as
offered**: the reset refuses, naming the obstacle and handing back a remedy that points at the file.
Deleting the database file and restarting returns the container to `200`, measured in step 6.

**The first instruction the operator receives is the wrong one, and this document records that rather
than asserting around it.** `/health` carries the generic initialisation-failure reason, which
recommends a database Reset; that reset then refuses. The correct remedy arrives on the second hop, out
of the refusal itself. Contrast
[`05`](05-degraded-pages-survive-a-migration-failure.md), where #326 gave the unwritable-directory
fault its own reason so the first instruction already names something that works. The selection
predicate for that reason matches SQLite error codes 14 and 8; an unreadable file raises neither, so it
falls through to the generic text. Reported as a finding, not fixed here: the degraded contract this
document asserts is intact either way, and changing a health reason is an application change rather
than test coverage.

## Canary

**The positive control this run produces for itself**, since the never-crash contract predates every
commit this document traces to and no pre-fix build applies. Step 6 removes the sabotage and reads
`200` in the same environment, so a build that degraded unconditionally fails there. Step 3's
`--expect 503` is the other half: it ends the run when the sabotage did not land, so the assertions in
steps 4 and 5 cannot pass against a healthy container.

**Where the canary has been run:** step 6 returned `200` on 2026-10-21 against `quotinator:local` at
commit `15f5b589`, in the same run that produced the readings above.

Deliberately not this document's own pass or fail: step 3 asserting `503` proves the sabotage landed,
not that the contract holds.

## Cleanup

```powershell
$dataDir = "C:\repos\Quotinator\.claude\temp\qt-startup-08"
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-08 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
```

**Expected:** the container is gone and the bind directory is removed, so the next run seeds from
nothing.
