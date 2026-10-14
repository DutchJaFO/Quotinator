# A recorded schema version ahead of the build degrades and says why

**Smoke:** no
**Environment:** Fresh
**Traces to:** #350, #327, #289

## Preconditions

**Beyond the profile.** The database is the one the Fresh profile's own startup creates, fully migrated,
then given one extra `System_ConsumerSchemaVersion` row while the container is stopped. The container
binds its data directory to a host path (`--bind`) so the row can be written from the host: a docker
volume would put the file somewhere `execute-sql.csx` cannot reach.

**This is a degradation scenario like every other document in this category.** It did not used to be:
#289 read an overshoot as a complete schema behind a stale counter, the state a migration squash leaves,
and had the application carry on. #350 reversed that, because nothing can tell that case apart from a
database that ran migrations this build has never seen. The migrations it never saw may have added,
altered or removed anything, so what the schema actually holds is unknown, and serving quote content
from it is a foot gun.

## Determinism

- **The unknown schema is the fault; the stale counter is only its symptom.** That is why `503` is the
  expected answer here and not an over-strict reading of a cosmetic mismatch. A build that serves from
  this database is not being lenient, it is vouching for a shape it has never seen.
- **The extra version is computed, never written as a literal.** Step 2 inserts `MAX(Version) + 1`, so
  the row is one beyond whatever this build actually migrated to. A literal would need re-deriving every
  time a milestone adds a migration, and would silently stop overshooting once the build caught up with
  it. This is the opposite choice from
  [`notifications-and-changelog/03`](../notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md),
  which necessarily names versions because its subject is a database sitting *between* two of them; here
  the subject is a relationship, so the relationship is what gets written.
- **The consumer counter is the one that moves.** `System_ConsumerSchemaVersion` tracks the app's own
  migrations, `System_SchemaVersion` tracks `Quotinator.Data`'s. Either overshooting sets the flag, and
  moving one keeps the construction minimal.
- **The row is added while the container is stopped, and stopped cleanly.** Writing to the database
  under a running container races the app's own connections. `docker stop -t 15` rather than `rm -f` is
  the same reasoning `test-env.csx` records for its own `reenter`: a SIGKILL leaves the `-wal` and
  `-shm` sidecars behind, so a row written from the host afterwards is a row written beside a journal
  the next startup still has to replay.
- **The second start waits for *listening*, not healthy.** Degraded is the expected outcome, so a
  readiness poll for healthy would never return and the document would fail at the wrong step, saying
  nothing about what the application actually did.
- **Without step 2 there is nothing to find.** Confirmed in-process by
  `StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_DegradesRatherThanServing`, which
  passes only against a version recorded one beyond what the database already holds: recorded at the
  level it already holds, the flag is never set, the application stays healthy, and every step below
  would be asserting against an ordinary install.
- **The notification is read from the page, not from `/api/v1/notifications`.** That route is not in
  `DatabaseHealthGateMiddleware`'s exempt list, so while degraded it answers `503` by design. The Blazor
  page is exempt and reads through `INotificationReader` in-process, which is what an operator actually
  opens.

## Steps

### 1. Create a healthy, fully migrated database

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
# A folder left by an earlier run holds a database, and step 6's single log match assumes none.
if (Test-Path $dataDir) { Remove-Item -LiteralPath $dataDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-startup-06 --port 18406 `
  --image quotinator:local --bind $dataDir
```

**Expected:** the environment reports healthy, leaving a fully migrated database in `$dataDir`.

**On failure:** if the build cannot reach healthy on a fresh database, nothing below is about
overshoot: stop and fix that first.

### 2. Stop the container cleanly and record a version one beyond this build

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
docker logs qt-startup-06 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
docker stop -t 15 qt-startup-06

dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" `
  --sql "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) SELECT MAX(Version) + 1, '2026-10-14 00:00:00' FROM System_ConsumerSchemaVersion;"
```

**Expected:** the log read before the stop finds nothing, the stop echoes the container name, then
`OK — 1 row(s) affected.` (the em dash is `execute-sql.csx`'s own output, quoted verbatim).

**On failure:** a SQL error means the overshoot state was never built, and every step below would be
asserting against an ordinary healthy database, which proves nothing. Stop.

### 3. Restart, and confirm the application degrades

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-06 --port 18406 `
  --image quotinator:local --bind $dataDir --wait-listening

$health = dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/health" --expect 503 | ConvertFrom-Json
"status=$($health.status)"
"namesTheOvershoot=$($health.reason -match 'schema version is ahead')"
"namesReset=$($health.reason -match 'Reset')"
"namesRestore=$($health.reason -match 'restore a backup')"
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/quotes/random" --expect 503 --status
```

**Expected:** `503` from `/health` with `status=unhealthy`, all three of `namesTheOvershoot`,
`namesReset` and `namesRestore` `True`, and `503` from the quote endpoint.

**On failure:** a `200` from either is the defect #350 exists to remove: the application vouching for a
schema whose shape it has never seen. A `503` whose reason names only the Reset leaves an operator who
wants their data back believing the route that discards it is the only one there is.

### 4. Confirm the degraded surface and the recovery routes stay reachable

```powershell
foreach ($p in '/', '/about', '/stats', '/notifications', '/import-review', '/rest-api') {
  $code = dotnet script scripts/testing/http.csx -- --url "http://localhost:18406$p" --status
  "$p -> $code"
}
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/openapi/v1.json" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/admin/database/seed/preview" --status
```

**Expected:** `200` from every page, `200` from the spec, and anything other than the gate's own
`unavailable` body from the admin route.

**On failure:** a `500` from a page means the degraded UI is rendering against a query it should not be
running, or not running one it safely could. A gated admin route means the remedy the reason names
cannot be reached, which makes the reason point nowhere.

### 5. Confirm the notification is raised and visible on the page

```powershell
$out = "$PWD\.claude\temp\qt-startup-06-shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$rowScript = @'
  const rows = [...document.querySelectorAll('tr')];
  const row = rows.find(r => r.textContent.includes('ahead of this build'));
  const r = row ? row.getBoundingClientRect() : { height: 0 };
  return { found: !!row,
           buttons: row ? [...row.querySelectorAll('button')].map(b => b.textContent.trim()) : [],
           namesRestore: !!row && /restore a backup/i.test(row.textContent),
           innerHeight: r.height };
'@
$page = $rowScript | dotnet script scripts/testing/capture-page.csx -- `
  --url "http://localhost:18406/notifications" --out "$out\notifications.png" --height 1100 `
  --script-stdin | ConvertFrom-Json
"found=$($page.found) innerHeight=$($page.innerHeight) namesRestore=$($page.namesRestore)"
"buttons=$($page.buttons -join ' | ')"
```

**Expected:** `found=True` with a non-zero `innerHeight`, `namesRestore=True`, and a `Reset the
database` button. The screenshot shows the row on the degraded page.

**This is the half of the defect that is easiest to lose.** The notification is written while the
database is unhealthy, which every other producer's `dbHealth.IsHealthy` gate would have suppressed,
and the page itself held a second gate that emptied its own list whenever the database was unhealthy.
`found=False` here, with step 3 passing, means one of those two gates is back: the application degraded
and then said nothing about why.

**On failure:** an empty page with a populated `/health` reason means the page gate is reading
`IsHealthy` again rather than `ExpectedSchemaIsPresent`.

### 6. Confirm the detection is in the log as well as the UI

```powershell
docker logs qt-startup-06 2>&1 | Select-String -Pattern 'schema version overshoot detected'
docker logs qt-startup-06 2>&1 | Select-String -SimpleMatch 'is ahead of this build'
docker logs qt-startup-06 2>&1 | Select-String -SimpleMatch 'treated as complete'
```

**Expected:** one match for the detection, at least one for the degraded reason, and **no** match for
`treated as complete`.

**On failure:** no match for the detection while step 5 passed means the notification came from
somewhere other than this startup, most likely a row left over from an earlier run against the same
directory: re-run from step 1 with a fresh `$dataDir`. A match for `treated as complete` means a
message is claiming the schema is intact, which this build has no way to know.

### 7. Confirm one of the two remedies actually resolves it

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/admin/database/reset" `
  --method POST --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/health" --expect 200 --status
```

**Expected:** `200` from the Reset, then `200` from `/health`: the rebuild recorded this build's own
version, so there is no overshoot left to degrade on.

**On failure:** a `409` means the Reset refused for want of a backup, which is a different fault with
its own documents in `backup/`. A `503` still answering afterwards means the Reset ran without
clearing the recorded state, so the application would stay degraded until the operator restarted it.

## Observed effect

**Measured 2026-10-14** against `quotinator:local`, on the first run of this document after #350, on a
database recorded at data v28, app v10 against a build knowing app v9.

`/health` answers `503` with:

> The database's recorded schema version is ahead of this build (data v28, app v10). This build does
> not know what the migrations it never saw changed, so the schema's shape is unknown and quote content
> is not served from it. Resolve it with a database Reset (POST /api/v1/admin/database/reset), which
> rebuilds the schema at this build's own version and discards what the database holds now, or restore
> a backup taken before the version moved ahead and restart.

`/api/v1/quotes/random` answers `503`, while all six exempt pages, the OpenAPI spec and the admin
surface answer normally. The log carries the detection once:

> `[Database - Init] schema version overshoot detected: recorded data v28 (known: v28), recorded app
> v10 (known: v9). This build does not know what the migrations it never saw changed, so the schema's
> shape is unknown: resolve it with a database Reset, or restore a backup taken before the version
> moved ahead`

and nothing matching `treated as complete`, the claim #289's message made and #350 removed.

**The version numbers above are observed output, not assertions.** They are recorded because an
operator reading a real log sees numbers there and should recognise the shape; no step compares against
them, and they move whenever a migration is added.

**The notifications page renders fully while degraded**, with the overshoot row first: *Action
required*, naming both remedies, carrying a `Reset the database` button, measured `innerHeight`
`179.39`. The whole list renders, not just that row, which is what the page gate used to prevent.

**The counts in the startup banner are real, not zero.** Measured `795 quotes`, `473 sources`,
`15 characters` on the degraded start. This fault completes `InitialiseAsync`, so it does not produce
the symptom
[`degraded-startup-shows-every-count-as-zero`](../../knowledgebase/degraded-startup-shows-every-count-as-zero.md)
describes, which belongs to a startup that never completes.

**The remedy works and clears itself.** `POST /api/v1/admin/database/reset` answers `200`, `/health`
returns to `200` in the same process, the API comes back out of the gate, and the overshoot
notification is gone: its `DatabaseReset` dismiss trigger resolved it.

## Cleanup

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
docker logs qt-startup-06 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-06 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$PWD\.claude\temp\qt-startup-06-shots" -ErrorAction SilentlyContinue
```
