# Degraded-state pages survive a genuine migration failure

**Smoke:** yes
**Environment:** Upgraded + Constrained
**Deterministic:** yes
**Traces to:** #293, #326, #327

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.2` tag**, chosen because it leaves a genuinely un-migrated database:
`System_Notification` does not exist on it at all. One container name (`qt-startup-05`) is reused across
the runs against one named volume, and the Constrained defect is **`/data` itself mounted read-only**
while a migration is pending.

**Two conditions have to hold together, and neither is sufficient alone** (measured 2026-08-27):

| `/data` | Migration pending | Result |
|---|---|---|
| Writable, root filesystem `--read-only` | yes | `200` healthy, which is #294's fix working |
| Read-only | no, already migrated | `200` healthy, with a `SQLite Error 8` logged |
| **Read-only** | **yes** | **`503` unhealthy, `SQLite Error 14`** |

**The third row is this test.** The initializer has real work to do and cannot write, so it degrades,
and `SQLite Error 14: 'unable to open database file'` is *the original incident's own error code*.

## Determinism

- **`--read-only-data`, not `--read-only`.** The root filesystem being unwritable is
  [`04-migration-replay-under-restricted-write.md`](04-migration-replay-under-restricted-write.md)'s
  subject and the application survives it by design. Using that flag here reproduces `04` and asserts
  the opposite of it.
- **The prior image must leave a migration pending.** Run this against a volume the current build has
  already upgraded and it reports `200` with only a `SQLite Error 8` in the log: the write fails, but
  there is no migration for it to fail *during*, so nothing degrades. That is why step 1 seeds from a
  published tag and step 2 never re-runs against its own output.
- **WAL sidecar state does not decide this.** Measured both ways, cleanly stopped so the sidecars are
  checkpointed away, and force-killed so they survive, and both degrade. #326 found sidecar state
  decisive for a read-only mount *without* a pending migration; that is a different question from this
  one, and this document does not depend on it.
- **`reenter` stops the previous container cleanly** (`docker stop -t 15`, not `rm -f`), so which of the
  two sidecar states this runs in is pinned rather than incidental.
- **`reenter --read-only-data` mounts the shared key ring read-only**, since the install it re-enters was
  created with that ring. The pages depend on it: with no key ring to read, every page answers `500`,
  which is [`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md)'s subject.
- **Wait for `unhealthy`, not for a `503`.** A starting app answers `503` too, from the startup wait
  page with `status=starting`, so step 2 polls the `status` field, the technique
  [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.
- **The page reads go through `capture-page.csx`.** It cannot read the browser console, so the console
  assertion is made from what a page script can read: every request the page issued, with its status
  (`performance.getEntriesByType('resource')`), and whether Blazor's own error banner is showing.
- **Never assert a count read while degraded, and never the count after the remedy either.** The first is
  a value read from a database the application could not open (index: *A degraded application is not
  expected to answer from its database*); the second moves with whatever the pending migration does to
  the content. Step 6 asserts only that content survived, a non-zero count.

**What this replaced, and why the old version could never pass.** Until 2026-08-27 this test forced the
failure with `--read-only` on the root filesystem, leaving `/data` writable, byte-identical to `04`'s
setup, which asserts the app is **healthy** under it. Two tests cannot both be right about the same
setup. Its own guard said *"health must be 503, confirming the test actually reached the failure
state"*, and that guard could never hold: measured `200` on 2026-08-18 and again on 2026-08-26.

The guard was the right instinct and is why the contradiction was provable rather than merely
suspected. What was missing was a step that *confirmed* the failure state before asserting against it,
and, as it turned out, a technique that produced one. The premise was never unreachable; the lever was
wrong.

## Explicitly not covered here

- **The whole data directory missing and uncreatable**, so that no key ring exists either. That is
  [`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md).
- **What the backup refusal offers, obstacle by obstacle.** That is `backup/04`'s subject.

## Steps

### 1. Seed a real, unmigrated v1.8.2 database

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-05 --port 18405 `
  --image ghcr.io/dutchjafo/quotinator:1.8.2

(Invoke-RestMethod "http://localhost:18405/api/v1/version").database.quotes
```

**Expected:** `quotes` is non-zero.

**On failure:** a partially seeded volume would make everything below meaningless. Stop and re-seed
rather than proceeding.

### 2. Start the current build with `/data` read-only, and confirm it degraded rather than died

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-05 --port 18405 `
  --image quotinator:local --read-only-data --wait-listening

# A 503's body is read from the response stream: PowerShell 5.1 leaves ErrorDetails.Message empty here.
function Health {
  try   { Invoke-RestMethod "http://localhost:18405/api/v1/health" }
  catch {
    $response = $_.Exception.Response
    if ($null -eq $response) { return [pscustomobject]@{ status = 'down' } }
    $body = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    try { $body | ConvertFrom-Json -ErrorAction Stop } catch { [pscustomobject]@{ status = 'unreadable' } }
  }
}
$deadline = (Get-Date).AddSeconds(180)
while (($health = Health).status -notin 'unhealthy', 'healthy' -and (Get-Date) -lt $deadline) { Start-Sleep 1 }

docker inspect -f '{{.State.Running}}' qt-startup-05
"status=$($health.status)"
"reasonNamesTheFault=$($health.reason -match 'data directory cannot be written')"
"reasonNamesAWorkableRemedy=$($health.reason -match 'Restore write access')"

$log = docker logs qt-startup-05 2>&1 | Out-String
"sqliteError14=$(([regex]::Matches($log, 'SQLite Error 14')).Count)"
```

**Expected:** `true` from `docker inspect`, `status=unhealthy`, `reasonNamesTheFault=True`,
`reasonNamesAWorkableRemedy=True`, and `sqliteError14` non-zero: the migration genuinely could not write,
which is the failure state every step below depends on.

**`true` and `unhealthy` together are the never-crash contract.** The reason is the half that has to be
checked rather than assumed: the *generic* initialisation-failure reason tells the operator to run a
database Reset, which writes and therefore cannot work on a read-only mount. #326 added a distinct
reason for exactly this fault so the advice points somewhere that can succeed. `unhealthy` alone would
pass just as happily with the misdirecting reason.

**On failure:** a container that is not running means the migration failure killed the process.
`status=healthy` means the volume was already migrated, so there was no pending migration for the
read-only mount to fail during: re-seed from the published tag rather than re-running step 2 against its
own output. `starting` or `down` means the wait gave up after 180 seconds.

### 3. Confirm the OpenAPI surface answers and every database read is gated

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/openapi/v1.json" --expect 200 --status

foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/conversations', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18405$path" --expect 503 --status
}
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**This is the recovery route's front door, and the API's refusal is its other half.** The spec is how
an operator discovers the documented way out; the seven reads refuse rather than answer from a database
the initializer could not open, because a `200` carrying zeroes cannot be told apart from an empty
database.

**On failure:** a `200` with a body from any of the seven means an endpoint answered from a database it
could not open. A `500` means it reached the database and threw rather than being gated.

### 4. Confirm the recovery route is reachable, and read what it offers

```powershell
$reset = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18405/api/v1/admin/database/reset" --method POST --expect 409 | ConvertFrom-Json
"reachedTheHandler=$($null -ne $reset.backupObstacle)"
"obstacle=$($reset.backupObstacle)"
"namesTheWorkableRemedy=$([bool](@($reset.remedies) -match 'Restore write access'))"
```

**Expected:** `409`, with `reachedTheHandler=True`, `obstacle=DestinationDirectoryNotWritable` and
`namesTheWorkableRemedy=True`.

**Reachable is not workable.** The `409` shows the request reached its handler rather than the health
gate. It cannot succeed: Reset writes, and the mount is what is read-only. The refusal hands back the
same remedy `/health` named, which step 6 proves works.

**On failure:** a `503` means the health gate answered instead of the handler. A `500` means the refusal
was never reached.

### 5. Read the three degraded pages

```powershell
$pageScript = @'
const text = (document.querySelector('main') || document.body).innerText;
const errorUi = document.getElementById('blazor-error-ui');
return {
  reasonShown: text.includes('data directory cannot be written'),
  stackTrace: /at [A-Za-z.]+\(/.test(document.body.innerText),
  errorUiShown: errorUi ? getComputedStyle(errorUi).display !== 'none' : null,
  failedStatuses: performance.getEntriesByType('resource')
    .filter(e => e.responseStatus >= 400).map(e => e.responseStatus)
};
'@

foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18405$path" --expect 200 --status
  $read = $pageScript | dotnet script scripts/testing/capture-page.csx -- `
    --url "http://localhost:18405$path" --out ".claude/temp/qt-startup-05$($path.Replace('/', '-')).png" `
    --script-stdin --wait-ms 4000 | ConvertFrom-Json
  "$path reasonShown=$($read.reasonShown) stackTrace=$($read.stackTrace) errorUiShown=$($read.errorUiShown) everyFailureIs503=$(@($read.failedStatuses | Where-Object { $_ -ne 503 }).Count -eq 0)"
}
```

**Expected:** `200` from each page, and for each one `reasonShown=True`, `stackTrace=False`,
`errorUiShown=False` and `everyFailureIs503=True`.

**This is the historical incident's own surface.** A v1.8.2 to v1.8.3-beta upgrade whose migration failed
left `/` and `/notifications` crashing instead of showing degraded UI. Each page now has to render the
real failure reason, carry no exception text, keep Blazor's error banner hidden, and make no failing
request other than the `503`s the gate is meant to answer.

**On failure:** a `500` from a page is the degraded UI failing to render. `errorUiShown=True` is a Blazor
circuit error on a page that otherwise answered `200`. A failed status other than `503` is a request the
page made that broke rather than being refused.

### 6. Apply the remedy, and confirm the container recovers with its content

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-05 --port 18405 `
  --image quotinator:local

dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/api/v1/health" --expect 200 --status
foreach ($path in '/api/v1/quotes', '/api/v1/quotes/random', '/api/v1/quotes/search?q=the',
                  '/api/v1/conversations', '/api/v1/notifications', '/api/v1/masterdata/sources',
                  '/api/v1/version') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18405$path" --expect 200 --status
}
foreach ($path in '/', '/stats', '/notifications') {
  dotnet script scripts/testing/http.csx -- --url "http://localhost:18405$path" --expect 200 --status
}
"quotes=$((Invoke-RestMethod 'http://localhost:18405/api/v1/version').database.quotes)"

$log = docker logs qt-startup-05 2>&1 | Out-String
"migrationApplied=$(([regex]::Matches($log, 'migration applied')).Count)"
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. `/health` and all three pages answer `200`, `quotes` is non-zero, and `migrationApplied` is
non-zero.

**This is the positive control and the remedy proof in one step.** Every assertion above is about a
degraded container, so without a passing case this document would stay green against a build that
degraded on everything. It also proves the remedy both `/health` and the refusal named: with write access
restored, the same volume migrates, and the content the read-only start could not touch is still there.

**On failure:** a `503` here means the degradation was not specific to the read-only mount, or the named
remedy does not work. `quotes=0` means the remedy cost the operator their data.

## Observed effect

**Measured 2026-10-23** against `quotinator:local` built from `eb8c1a19`, upgrading a `1.8.2` volume
seeded to 799 quotes.

The process stays up and `/health` settles at `503 unhealthy`, the first read already settled, with the
data-directory reason and fourteen `SQLite Error 14` lines in the log. `/openapi/v1.json` answers `200`
and all seven database reads answer `503`. The reset reaches its handler and refuses with `409`, obstacle
`DestinationDirectoryNotWritable`, naming *restore write access* first. Its second remedy, *Retry with
`allowNoBackup=true`*, cannot work on a read-only mount either, and is measured as an unhandled `500`
in [`10`](10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md).

Each page renders the reason, carries no stack trace, keeps Blazor's error banner hidden, and makes
exactly two failing requests, both `503`. What an operator sees on `/`, verbatim:

> Quotinator started with a problem
>
> The data directory cannot be written. This usually means the volume is mounted read-only, or the
> container user lacks write permission on it. Restore write access to the data directory and restart.
> A database Reset cannot resolve this: it writes too.
>
> The database was left at its last known-good state: Quotes 0, Sources 0, Characters 0, People 0,
> Series 0, Seasons 0, Universes 0, Stage directions 0, Sound cues 0, Conversations 0.

**The zeroes are wrong, and this document does not assert them.** The database holds 799 quotes; the
counts are read from a database the application could not open, which is
[#428](https://github.com/DutchJaFO/Quotinator/issues/428)'s subject: withhold the counts rather than
show zero. `/stats` renders the same zeroes. Every page also offers *Reset the database*, a button that
cannot succeed on this mount, as step 4 shows.

That error code matters. The original incident, a live HA v1.8.2 to v1.8.3-beta upgrade whose migration
failed partway through, leaving `NotificationSummary` (embedded in Home's modal) and `/notifications`
crashing instead of showing degraded UI, reported exactly `SQLite Error 14`. The `--read-only`-root
technique this replaced could not reproduce it: that arrangement denies writes at a different syscall
and produced `SQLite Error 10: 'disk I/O error'` when it degraded at all. Denying `/data` reproduces the
incident's code exactly.

**Recovery route, and whether it can succeed.** Reachable: the pages, `/openapi/v1.json` and the reset
handler all answer. **A Reset cannot fix this one**: it writes, and the mount is what is read-only. The
remedy is outside the container, remount the volume writable or fix its permissions, and restart; doing
that applied the pending migration and returned `200` with 795 quotes, measured in step 6.

`System_Notification` genuinely does not exist on a real v1.8.2 database (confirmed live:
`SELECT name FROM sqlite_master WHERE type='table' AND name='System_Notification'` returns no rows), so
this setup exercises `NotificationReader`'s missing-table fix and `DatabaseStatsSummary`'s
degraded-skip fix together, which is what the test is for.

## Canary

Build the commit before [#293](https://github.com/DutchJaFO/Quotinator/issues/293)'s first code
change, under a distinct tag, and run steps 2 to 5 against it.

Step 5 must fail: the degraded pages are what #293 made survivable, so on that build reading `/` and
`/notifications` during a migration failure does not produce a rendered page. Record what it produces
instead, since an unhandled error and a blank response are different defects.

**Step 2 is the precondition and the control, and this document was rewritten because an earlier
technique could not reach it.** `sqliteError14` must be non-zero: a `--read-only` root leaves `/data`
writable and the migration succeeds, so the failure state is reached by denying `/data` itself with a
migration pending. A run reporting a healthy start has not reached the premise.

Step 6 is the positive control inside every run: it removes the sabotage and reads `200`, so a build
that degraded unconditionally fails there.

Remove the container, image and worktree afterwards.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-05
```

**Expected:** the container and its volume are gone, so the next run seeds from nothing.
