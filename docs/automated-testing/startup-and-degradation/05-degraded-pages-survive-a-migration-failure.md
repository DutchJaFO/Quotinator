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

**The test is `/data` read-only with a migration pending.** The initializer has real work to do and
cannot write, which is the state the original incident's pages crashed in. A writable `/data` under a
read-only root filesystem is a different setup and stays healthy, which is
[`04`](04-migration-replay-under-restricted-write.md)'s subject.

## Determinism

- **`--read-only-data`, not `--read-only`.** The root filesystem being unwritable is
  [`04-migration-replay-under-restricted-write.md`](04-migration-replay-under-restricted-write.md)'s
  subject and the application survives it by design. Using that flag here reproduces `04` and asserts
  the opposite of it.
- **The prior image must leave a migration pending.** A read-only `/data` degrades without one as well,
  with the same reason, because `keys/` is read-only with it. So `unhealthy` does not show a migration
  was pending, and only step 6 finding one applied does. That is why step 1 seeds from a published tag
  and step 2 never re-runs against its own output.
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
- **Never assert a count, while degraded or after the remedy.** The first is a value read from a
  database the application could not open (index: *A degraded application is not expected to answer
  from its database*); the second depends on the bundled sources, which this test does not own. Step 6
  asserts only that the test's own sentinel quote survived.

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

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-05-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-05 --port 18405 --image ghcr.io/dutchjafo/quotinator:1.8.2
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/import --method POST --file docs/automated-testing/startup-and-degradation/fixtures/sentinel-quote.json --expect 200
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/quotes/5e7a1c3d-9b2f-4e6a-8d0c-b1f2a3e4c5d6 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\sentinel-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\sentinel-before.json --equals source="Quotinator Test Harness"
```

**Expected:** the import answers `200`, and the `json` command exits `0`: the test's own
[sentinel quote](fixtures/sentinel-quote.json) is stored and readable by its id.

**The sentinel is this document's own data.** The bundled sources are seeded too, since the image always
seeds them, but nothing below asserts on what they contain: that changes whenever a source or a
correction does. Step 6 asks only whether the sentinel survived.

**On failure:** a sentinel that cannot be stored or read leaves step 6 with nothing to prove. Stop.

### 2. Start the current build with `/data` read-only, and confirm it degraded rather than died

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-05 --port 18405 --image quotinator:local --read-only-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18405/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\health.json --equals status=unhealthy --contains "reason=data directory cannot be written" --contains "reason=Restore write access"
docker inspect -f '{{.State.Running}}' qt-startup-05
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-05 --pattern "thrown: SqliteException" --at-least 1
```

**Expected:** the `json` command exits `0`: `status = unhealthy`, the reason naming the fault and the
workable remedy. `docker inspect` prints `true`, and the `log` command exits `0`: the database itself
refused the write.

**The reason alone does not show the database failed.** A read-only `keys/` produces the same reason
from the write probe, before the database is opened, so the `SqliteException` is what shows the failure
state every step below depends on. It is counted by type, never by message or error code. That a
migration was what could not run is shown in step 6, which finds it applied once writing is possible.

**`true` and `unhealthy` together are the never-crash contract.** The reason is the half that has to be
checked rather than assumed: the *generic* initialisation-failure reason tells the operator to run a
database Reset, which writes and therefore cannot work on a read-only mount. #326 added a distinct
reason for exactly this fault so the advice points somewhere that can succeed. `unhealthy` alone would
pass just as happily with the misdirecting reason.

**On failure:** a container that is not running means the migration failure killed the process.
`status=healthy` means the mount did not take effect, so nothing was refused. No `SqliteException` means
the reason came from the write probe alone and the database was never reached, so the steps below would
not be reading a failed migration. `starting` or `down` means the wait gave up after 180 seconds.

### 3. Confirm the OpenAPI surface answers and every database read is gated

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/version --expect 503 --status
```

**Expected:** `200` from `/openapi/v1.json`, and `503` from every one of the seven endpoints.

**This is the recovery route's front door, and the API's refusal is its other half.** The spec is how
an operator discovers the documented way out; the seven reads refuse rather than answer from a database
the initializer could not open, because a `200` carrying zeroes cannot be told apart from an empty
database.

**On failure:** a `200` with a body from any of the seven means an endpoint answered from a database it
could not open. A `500` means it reached the database and threw rather than being gated.

### 4. Confirm the recovery route is reachable, and read what it offers

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/admin/database/reset --method POST --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\reset.json --equals backupObstacle=DestinationDirectoryNotWritable --contains "remedies=Restore write access"
```

**Expected:** `409`, and the `json` command exits `0`: the obstacle is
`DestinationDirectoryNotWritable`, which only the handler can report, and a remedy names restoring
write access.

**Reachable is not workable.** The `409` shows the request reached its handler rather than the health
gate. It cannot succeed: Reset writes, and the mount is what is read-only. The refusal hands back the
same remedy `/health` named, which step 6 proves works.

**On failure:** a `503` means the health gate answered instead of the handler. A `500` means the refusal
was never reached.

### 5. Read the three degraded pages

Each page is read by the same committed page script,
[`degraded-page-unwritable-data.js`](../../../scripts/testing/page-scripts/degraded-page-unwritable-data.js).

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/ --expect 200 --status
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18405/ --out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\home.png --script-file scripts/testing/page-scripts/degraded-page-unwritable-data.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\home.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\home.json --equals reasonShown=true --equals stackTrace=false --equals errorUiShown=false --equals failuresOtherThan503=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/stats --expect 200 --status
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18405/stats --out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\stats.png --script-file scripts/testing/page-scripts/degraded-page-unwritable-data.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\stats.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\stats.json --equals reasonShown=true --equals stackTrace=false --equals errorUiShown=false --equals failuresOtherThan503=0
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/notifications --expect 200 --status
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18405/notifications --out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\notifications.png --script-file scripts/testing/page-scripts/degraded-page-unwritable-data.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\notifications.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\notifications.json --equals reasonShown=true --equals stackTrace=false --equals errorUiShown=false --equals failuresOtherThan503=0
```

**Expected:** `200` from each page, and each `json` command exits `0`: the reason shown, no stack
trace, Blazor's error banner hidden, and no failed request other than a `503`.

**This is the historical incident's own surface.** A v1.8.2 to v1.8.3-beta upgrade whose migration failed
left `/` and `/notifications` crashing instead of showing degraded UI. Each page now has to render the
real failure reason, carry no exception text, keep Blazor's error banner hidden, and make no failing
request other than the `503`s the gate is meant to answer.

**On failure:** a `500` from a page is the degraded UI failing to render. `errorUiShown = true` is a Blazor
circuit error on a page that otherwise answered `200`. A failed status other than `503` is a request the
page made that broke rather than being refused.

### 6. Apply the remedy, and confirm the container recovers with its content

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-05 --port 18405 --image quotinator:local
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18405/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/version --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/quotes/5e7a1c3d-9b2f-4e6a-8d0c-b1f2a3e4c5d6 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\sentinel-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\sentinel-after.json --equals source="Quotinator Test Harness"
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/ --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/stats --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18405/api/v1/notifications?pageSize=0 --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-05-out\notifications-api.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-05-out\notifications-api.json --contains items=migrationapplied
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. `/health` and all three pages answer `200`, and both TestProbe commands exit `0`: the sentinel quote from step 1 is still there, and the notifications list holds a notification of kind `migrationapplied`, which the application writes for each migration it applies.

**This is the positive control and the remedy proof in one step.** Every assertion above is about a
degraded container, so without a passing case this document would stay green against a build that
degraded on everything. It also proves the remedy both `/health` and the refusal named: with write access
restored, the same volume migrates, and the content the read-only start could not touch is still there.

**On failure:** a `503` here means the degradation was not specific to the read-only mount, or the named
remedy does not work. A missing sentinel means the remedy cost the operator their data.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `870a0502`, upgrading a `1.8.2` volume
holding the sentinel quote. The paragraph on a read-only `/data` with no migration pending is from the
2026-10-31 run against `00a9dfe2`; this run did not repeat it.

The process stays up and `/health` settles at `503 unhealthy` with the data-directory reason:

> The data directory cannot be written. This usually means the volume is mounted read-only, or the
> container user lacks write permission on it. Restore write access to the data directory and restart.
> A database Reset cannot resolve this: it writes too.

The log holds 12 `SqliteException` throws. `/openapi/v1.json` answers `200` and all seven database reads
answer `503`. The reset reaches its handler and refuses with `409`, obstacle
`DestinationDirectoryNotWritable`, naming *restore write access* first and *retry with
`allowNoBackup=true`* second.

`/`, `/stats` and `/notifications` each answer `200`, render the reason, carry no stack trace, keep
Blazor's error banner hidden, and make no failing request other than a `503`.

**A read-only `/data` with no migration pending degrades too.** Measured on 2026-10-31 by creating a
database on this build and re-entering it with `--read-only-data`: `503 unhealthy`, the same reason.

**Recovery route, and whether it can succeed.** Reachable: the pages, `/openapi/v1.json` and the reset
handler all answer. **A Reset cannot fix this one**: it writes, and the mount is what is read-only. The
remedy is outside the container, remount the volume writable or fix its permissions, and restart. Doing
that returned `200` from `/health`, all seven reads and all three pages, with the sentinel quote present
and one `migrationapplied` notification per migration applied.

The original incident was a live HA v1.8.2 to v1.8.3-beta upgrade whose migration failed partway through,
leaving `NotificationSummary` (embedded in Home's modal) and `/notifications` crashing instead of showing
degraded UI.

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
technique could not reach it.** `/health` must read `unhealthy` with the container running: a
`--read-only` root leaves `/data` writable and the migration succeeds, so the failure state is reached by
denying `/data` itself with a migration pending. A run reporting a healthy start has not reached the
premise. On a canary this is the whole control: neither commit below has the exception channel, so
step 2's `SqliteException` count reads `0` there and says nothing, and the commit before #293 also
predates #326's reason.

Step 6 is the positive control inside every run: it removes the sabotage and reads `200`, so a build
that degraded unconditionally fails there.

Remove the container, image and worktree afterwards.

**Where the canary has been run, 2026-10-31.** Against `64297b7e`, the commit before #293, as `quotinator:canary-293`: step 2 read `unhealthy` with the generic reason and the container running, and step 5 read `500` from all three pages. Against `e4078444`, the commit before #327, as `quotinator:canary-327`: step 2 read `unhealthy` with the data-directory reason, step 3 read `200` from `/api/v1/version`, and step 4 `500` from the reset.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-05
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-05-out
```

**Expected:** the container, its volume and the output directory are gone, so the next run seeds from
nothing.
