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
- **Nothing read from the database is shown while degraded, and that is asserted rather than assumed.**
  An overshot schema is of unknown shape, so its content is untrusted whether or not a query against it
  would execute: the notifications page, the import-review page and the file history all render their
  degraded empty state. The operator's explanation comes from `StartupErrorModal`'s reason, which is
  in-memory state and cannot depend on a row that may never have been written.

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

### 5. Confirm the operator is told why, and that nothing from the database is shown

```powershell
$out = "$PWD\.claude\temp\qt-startup-06-shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# Substring matches rather than regex literals: a /pattern/ inside a here-string is read as a path by
# some shells before it ever reaches the browser.
$modalScript = @'
  const modal = document.querySelector('.modal');
  const text  = (modal ? modal.innerText : '').toLowerCase();
  return { modal: !!modal,
           namesTheOvershoot: text.includes('schema version is ahead'),
           namesReset: text.includes('reset'),
           namesRestore: text.includes('restore a backup'),
           notificationRows: modal ? modal.querySelectorAll('.notification-body').length : -1 };
'@
$modalRead = $modalScript | dotnet script scripts/testing/capture-page.csx -- `
  --url "http://localhost:18406/" --out "$out\degraded-home.png" --width 1280 --height 1100 `
  --script-stdin | ConvertFrom-Json
"modal=$($modalRead.modal) overshoot=$($modalRead.namesTheOvershoot) reset=$($modalRead.namesReset) restore=$($modalRead.namesRestore) rows=$($modalRead.notificationRows)"

$pageScript = @'
  return { rows: document.querySelectorAll('tbody tr').length,
           bodyCells: document.querySelectorAll('.notification-body').length,
           emptyState: document.body.innerText.toLowerCase().includes('no notifications yet') };
'@
$pageRead = $pageScript | dotnet script scripts/testing/capture-page.csx -- `
  --url "http://localhost:18406/notifications" --out "$out\degraded-notifications.png" --height 1100 `
  --script-stdin | ConvertFrom-Json
"notificationsPage rows=$($pageRead.rows) bodyCells=$($pageRead.bodyCells) emptyState=$($pageRead.emptyState)"
```

**Expected:** the modal renders, its reason names the overshoot and both remedies, and
`notificationRows=0`. The notifications page reads `rows=0`, `bodyCells=0` and `emptyState=True`
(*No notifications yet.*). Both screenshots record it.

**`$home` is read-only in PowerShell 5.1**, so the modal reading is held in `$modalRead`: assigning
`$home` fails the step with a variable error that has nothing to do with the application.

**The reason is the only channel that can be relied on here, and that is the point of this step.** An
overshot schema is of unknown shape, so every row read from it is untrusted content, the notification
explaining the overshoot included: this build cannot even assume that row was written, since the
`INSERT` ran against a table it cannot vouch for. The reason text is in-memory state, so it is there
whatever the database turns out to hold.

**On failure:** any notification row rendering on either surface means a read gate is back to asking
whether the query *would execute* rather than whether its answer can be trusted. A modal with no
reason means the degradation was recorded without one.

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

### 7. Repair it from the degraded pages themselves

The operator's own route, and a driver step: the reason names a Reset, and the degraded pages are where
they run one without reaching for an API client. Every page that can carry an interactive control shows
the same banner, because the modal is dismissed once and the repair must not go with it.

```powershell
$js = @'
const sleep = ms => new Promise(r => setTimeout(r, ms));
const resets = () => [...document.querySelectorAll('button')].filter(b => b.textContent.trim() === 'Reset the database').length;
for (let i = 0; i < 40 && resets() === 0; i++) await sleep(250);
return { resetButtons: resets(), reasonShown: document.body.innerText.includes('schema version is ahead') };
'@
foreach ($p in '/rest-api','/notifications','/import-review','/stats','/about') {
  $r = $js | dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18406$p" `
         --out "$out\degraded-$($p.Trim('/')).png" --width 1280 --height 900 --script-stdin | ConvertFrom-Json
  "$p -> resetButtons=$($r.resetButtons) reasonShown=$($r.reasonShown)"
}
```

**Expected:** `resetButtons=1` and `reasonShown=True` on `/rest-api`, `/notifications`,
`/import-review` and `/stats`; `0` and `False` on `/about`, which is static SSR and carries no
interactive control. `/rest-api` is where the modal's Continue lands and is reachable from the nav
anywhere, so no degraded page is more than one click from the repair.

**On failure:** a `0` on any of the first four means that page is back to naming a remedy the operator
cannot run from it. `/about` reading `1` is not a failure, but it means that page became interactive,
which is a change worth knowing about.

```powershell
"quotes before: $((Invoke-RestMethod 'http://localhost:18406/api/v1/version').database.quotes)"
```

Then open `http://localhost:18406/` and, against the modal:

```js
const sleep = ms => new Promise(r => setTimeout(r, ms));
const modal = () => document.querySelector('.modal');
const btn = t => modal() ? [...modal().querySelectorAll('button')].find(b => b.textContent.trim() === t) : null;
for (let i = 0; i < 40 && !modal(); i++) await sleep(250);
const before = [...modal().querySelectorAll('button')].map(b => b.textContent.trim());
btn('Reset the database').click(); await sleep(1500);
const armed = [...modal().querySelectorAll('button')].map(b => b.textContent.trim());
btn('Cancel').click(); await sleep(1200);
return { before, armed, afterCancel: [...modal().querySelectorAll('button')].map(b => b.textContent.trim()) };
```

**Expected:** `before` is `Reset the database, Continue`, `armed` is `Confirm, Cancel, Continue`, and
`afterCancel` returns to `before`. The quote count is unchanged: Cancel called nothing.

Then click **Reset the database** and **Confirm**, and read the result:

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/health" --expect 200 --status
"quotes after: $((Invoke-RestMethod 'http://localhost:18406/api/v1/version').database.quotes)"
```

**Expected:** `200` from `/health` and `quotes after: 0`. The rebuild recorded this build's own
version, so there is no overshoot left to degrade on, and the page reloads into the normal UI.

**The quote count is what separates Cancel from Confirm**, as it is on the Notifications page: both
leave the modal looking similar, and only the domain read says which one ran.

**On failure:** no `Reset the database` button means the degraded page has no repair control at all,
and the reason names a route the operator cannot take from the UI. A `409` from the equivalent API call
means the Reset refused for want of a backup, which is a different fault with its own documents in
`backup/`; the modal reports that refusal in place of reloading.

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

**The modal carries the reason and no database content.** The notifications page shows its empty
state, and the modal's own summary lists no rows: an overshot schema's content is untrusted, so none
of it is shown, and what the operator reads is the in-memory reason above it.

**The counts are populated rather than zero on this path, and are no more trustworthy for it.**
Measured `795 quotes`, `473 sources`, `15 characters` on the degraded start, because this fault
completes `InitialiseAsync` where the faults in
[`degraded-startup-shows-every-count-as-zero`](../../knowledgebase/degraded-startup-shows-every-count-as-zero.md)
never do. [#428](https://github.com/DutchJaFO/Quotinator/issues/428) owns withholding them on both
surfaces, and this path is one more reason it should: a real-looking number read from a schema this
build cannot vouch for is the most convincing wrong answer of the three.

**The remedy works from the pages, not only from an API client.** Measured 2026-10-14: the modal
offers `Reset the database` next to `Continue`, arming to `Confirm, Cancel, Continue`; Cancel leaves
`quotes` at `795`, and Confirm takes `/health` from `503` to `200` with `quotes` at `0` and the page
reloaded into the normal UI. The same control appears on `/rest-api`, `/notifications`,
`/import-review` and `/stats`, above the reason; running it from `/notifications` produced the same
`503` to `200` and `795` to `0`. `/about` carries none: it is static SSR. The equivalent
`POST /api/v1/admin/database/reset` answers `200` too, and the API comes back out of the gate with it.

## Cleanup

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-06-data"
docker logs qt-startup-06 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-06 --bind $dataDir
Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$PWD\.claude\temp\qt-startup-06-shots" -ErrorAction SilentlyContinue
```
