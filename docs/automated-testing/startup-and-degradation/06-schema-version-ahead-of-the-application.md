# A recorded schema version ahead of the build degrades and says why

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #350, #327, #289
**Fully green after:** [#445](https://github.com/DutchJaFO/Quotinator/issues/445): step 6's
*nothing thrown since Cancel* `log` command reads `TaskCanceledException` lines logged after the page
that confirmed the Reset closes, until the application stops logging a circuit the client closed as an
error.

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
- **The second start waits for *listening*, then for `unhealthy`, never for healthy or for a `503`.**
  Degraded is the expected outcome, so a readiness poll for healthy would never return and the document
  would fail at the wrong step, and the startup wait page answers `503` with `status=starting`, so a wait
  on the status code can return before the degraded state is settled. Step 3 polls the `status` field,
  the technique [`01`](01-seeding-backup-degraded-startup-and-reset-recovery.md) established.
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

A folder left by an earlier run holds a database that is already overshot or already repaired, and step
1 must start from none, so it is deleted first.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-06-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-06-shots --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-06-data
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-06 --port 18406 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-06-data
```

**Expected:** the environment reports healthy, leaving a fully migrated database in the bind directory.

**On failure:** if the build cannot reach healthy on a fresh database, nothing below is about
overshoot: stop and fix that first.

### 2. Stop the container cleanly and record a version one beyond this build

```
docker stop -t 15 qt-startup-06
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-startup-06-data\quotinatordata.db --sql "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) SELECT MAX(Version) + 1, '2026-10-14 00:00:00' FROM System_ConsumerSchemaVersion;"
```

**Expected:** the stop echoes the container name, then `OK — 1 row(s) affected.` (the em dash is
`execute-sql.csx`'s own output, quoted verbatim).

**On failure:** a SQL error means the overshoot state was never built, and every step below would be
asserting against an ordinary healthy database, which proves nothing. Stop.

### 3. Restart, and confirm the application degrades

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-06 --port 18406 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-06-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18406/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\health.json --equals status=unhealthy --contains "reason=schema version is ahead" --contains reason=Reset --contains "reason=restore a backup"
docker inspect -f '{{.State.Running}}' qt-startup-06
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/version --expect 503 --status
```

**Expected:** the `json` command exits `0`: `status = unhealthy`, the reason naming the overshoot, the
Reset and restoring a backup. `docker inspect` prints `true`, and every one of the seven database reads
answers `503`.

**`true` and `unhealthy` together are the never-crash contract** for a database this build cannot vouch
for. The seven reads refuse because a schema of unknown shape is untrusted whether or not a given query
against it would execute.

**On failure:** a container that is not running means the overshoot killed the process. `status=healthy`
or a `200` from any read is the defect #350 exists to remove: the application vouching for a schema whose
shape it has never seen. A reason naming only the Reset leaves an operator who wants their data back
believing the route that discards it is the only one there is. `starting` or `down` means the wait gave
up after 180 seconds.

### 4. Confirm the degraded surface and the recovery routes stay reachable

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/ --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/about --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/stats --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/import-review --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/rest-api --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/admin/backups --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\backups.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\backups.json --present items
```

**Expected:** `200` from every page, `200` from the spec, and `200` from the backup listing, whose
`json` command exits `0` on its `items` field.

**The backup listing is the recovery surface's probe, deliberately.** The reason names two remedies,
a Reset (step 6) and restoring a backup taken before the version moved ahead; the listing is how an
operator finds that backup, and it answers off the filesystem, so it reads nothing from the untrusted
database. An earlier version of this step probed `GET /api/v1/admin/database/seed/preview`, which reads
the database, so a `200` from it was a value served from untrusted content, and #453 gates it.

**On failure:** a `500` from a page means the degraded UI is rendering against a query it should not be
running. A `503` from the listing means the recovery surface is gated, so the remedies the reason names
cannot be reached from the API.

### 5. Confirm the operator is told why, and that nothing from the database is shown

The page scripts are committed files:
[`overshoot-modal.js`](../../../scripts/testing/page-scripts/overshoot-modal.js) and
[`notifications-page-rows.js`](../../../scripts/testing/page-scripts/notifications-page-rows.js).

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/ --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-home.png --width 1280 --height 1100 --script-file scripts/testing/page-scripts/overshoot-modal.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-home.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-home.json --equals modal=true --equals namesTheOvershoot=true --equals namesReset=true --equals namesRestore=true --equals notificationRows=0
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/notifications --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-notifications.png --height 1100 --script-file scripts/testing/page-scripts/notifications-page-rows.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-notifications.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-notifications.json --equals rows=0 --equals bodyCells=0 --equals emptyState=true
```

**Expected:** both `json` commands exit `0`. The modal renders, its reason names the overshoot and both
remedies, and it carries no notification row. The notifications page has no rows and no body cells, and
shows its empty state (*No notifications yet.*). Both screenshots record it.

**The reason is the only channel that can be relied on here, and that is the point of this step.** An
overshot schema is of unknown shape, so every row read from it is untrusted content, the notification
explaining the overshoot included: this build cannot even assume that row was written, since the
`INSERT` ran against a table it cannot vouch for. The reason text is in-memory state, so it is there
whatever the database turns out to hold.

**On failure:** any notification row rendering on either surface means a read gate is back to asking
whether the query *would execute* rather than whether its answer can be trusted. A modal with no
reason means the degradation was recorded without one.

**Nothing here reads a log line's wording.** The detection is shown by `/health`'s reason and the
modal's, both read above from the application's own responses, and covered in-process by
`StartupResilienceTests.Startup_SchemaVersionAheadOfApplication_HealthReasonNamesTheOvershoot`. A step
that matched the log's wording used to follow; wording changes with the application, and a test that
reads it breaks for a reason that has nothing to do with the behaviour it is about.

### 6. Repair it from the degraded pages themselves

The operator's own route, and a driver step: the reason names a Reset, and the degraded pages are where
they run one without reaching for an API client. Every page that can carry an interactive control shows
the same banner, because the modal is dismissed once and the repair must not go with it.

Each page is read by the same committed page script,
[`overshoot-reset-control.js`](../../../scripts/testing/page-scripts/overshoot-reset-control.js):

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/rest-api --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-rest-api.png --width 1280 --height 900 --script-file scripts/testing/page-scripts/overshoot-reset-control.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-rest-api.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-rest-api.json --equals resetButtons=1 --equals reasonShown=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/notifications --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-notifications-page.png --width 1280 --height 900 --script-file scripts/testing/page-scripts/overshoot-reset-control.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-notifications-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-notifications-page.json --equals resetButtons=1 --equals reasonShown=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/import-review --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-import-review.png --width 1280 --height 900 --script-file scripts/testing/page-scripts/overshoot-reset-control.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-import-review.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-import-review.json --equals resetButtons=1 --equals reasonShown=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/stats --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-stats.png --width 1280 --height 900 --script-file scripts/testing/page-scripts/overshoot-reset-control.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-stats.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-stats.json --equals resetButtons=1 --equals reasonShown=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/about --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-about.png --width 1280 --height 900 --script-file scripts/testing/page-scripts/overshoot-reset-control.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-about.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\degraded-about.json --equals resetButtons=0 --equals reasonShown=false
```

**Expected:** every `json` command exits `0`: one Reset button and the reason shown on `/rest-api`,
`/notifications`, `/import-review` and `/stats`; neither on `/about`, which is static SSR and carries no
interactive control. `/rest-api` is where the modal's Continue lands and is reachable from the nav
anywhere, so no degraded page is more than one click from the repair.

**On failure:** a `0` on any of the first four means that page is back to naming a remedy the operator
cannot run from it. `/about` reading `1` is not a failure, but it means that page became interactive,
which is a change worth knowing about.

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/health --expect 503 --status
```

**The quote count cannot be read here, and that is the contract working rather than a gap.** This line
read `(Invoke-RestMethod '.../api/v1/version').database.quotes` until 2026-10-21, when #428 stopped
that endpoint reporting counts on a degraded database: it answers `503` with `status: degraded` and no
`database` block, since zeroes read off a database the initializer could not vouch for are
indistinguishable from a genuinely empty one. No endpoint serves a count while degraded, by the index
rule *A degraded application is not expected to answer from its database*, so `/health` is what
separates the two outcomes below: still `503` after Cancel, `200` after Confirm.

Then, against the home page's modal, arm the Reset and cancel it:

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/ --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\modal-after-cancel.png --width 1280 --height 1100 --script-file scripts/testing/page-scripts/overshoot-modal-cancel.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\modal-after-cancel.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\modal-after-cancel.json --equals "before=Reset the database, Continue" --equals "armed=Confirm, Cancel, Continue" --equals "afterCancel=Reset the database, Continue"
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/health --expect 503 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-06 --mark C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-cancel.mark
```

**Expected:** the `json` command exits `0`: the buttons read `Reset the database, Continue`, then
`Confirm, Cancel, Continue` once armed, and return to the first after Cancel. `/health` still reads
`503`: Cancel called nothing, so the overshoot is still there. The mark records where the log stands, so
the repair below is counted on its own.

Then arm it again and confirm. The banner clears itself, in place, with no page reload:

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18406/ --out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-confirm.png --width 1280 --height 1100 --script-file scripts/testing/page-scripts/overshoot-modal-confirm.js --result-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-confirm.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-confirm.json --equals reasonGone=true
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/quotes --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18406/api/v1/quotes/search?q=the" --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/conversations --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/masterdata/sources --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18406/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\version-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\version-after.json --equals database.quotes=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-06 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-cancel.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18406/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-06 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-cancel.mark --pattern "thrown: JsonReaderException" --at-least 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-06 --mark C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-control.mark
```

**Expected:** the seven database reads gated while degraded each answer `200` here, which proves every one of them is a real endpoint rather than a path the gate refuses whatever it is. The first `json` command exits `0` on the reason gone, `/health` answers `200`, the quote count is `0`, and the exception count is unchanged since Cancel. The malformed upload answers `422`, and the `log` command after it exits `0` on at least one `JsonReaderException` thrown since Cancel.

**The malformed upload is the zero count's positive control.** A log with no exception in it is not
proof that nothing was thrown: it proves it only once the same run has shown the channel recording one.
The upload throws a `JsonReaderException` the application expects and answers `422` for, the same defect
[`api-surface/05`](../api-surface/05-a-thrown-exception-is-logged.md) uses, from the committed
[`malformed-import.json`](fixtures/malformed-import.json). It comes after the zero count so it cannot
raise it, and the second mark lets the cleanup count from after it.
The rebuild recorded this build's own version, so there is no overshoot left to degrade on, and the
banner and its button disappear where they stand. This is the positive control: the same container,
repaired by the remedy the reason named, answers `200`.

**The exception count is an assertion, not a detail.** The repair used to end in a forced page reload,
and a full reload of an interactive page tears the Blazor circuit down without a close handshake:
measured 2026-10-14, four `WebSocketException`s at `Error` for every reload, so a repair that worked
wrote four error lines into the log an operator reads to find out what is wrong. The surfaces now
re-render from `DatabaseHealthState.Changed` instead. A `WebSocketException` here means a reload is back.

**On failure:** no `Reset the database` button means the degraded page has no repair control at all,
and the reason names a route the operator cannot take from the UI. A `409` from the equivalent API call
means the Reset refused for want of a backup, which is a different fault with its own documents in
`backup/`; the modal reports that refusal in place of reloading.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`, every step run unattended.

The container stays running and `/health` answers `503` with:

> The database's recorded schema version is ahead of this build (data v28, app v10). This build does
> not know what the migrations it never saw changed, so the schema's shape is unknown and quote content
> is not served from it. Resolve it with a database Reset (POST /api/v1/admin/database/reset), which
> rebuilds the schema at this build's own version and discards what the database holds now, or restore
> a backup taken before the version moved ahead and restart.

The version numbers are observed output, not assertions: they move whenever a migration is added.

All seven database reads answer `503`. The six pages, the spec and `GET /api/v1/admin/backups` answer
`200`. The modal names the overshoot and both remedies with no notification row, and the notifications
page shows its empty state.

**The remedy works from the pages.** One Reset button and the reason appear on `/rest-api`,
`/notifications`, `/import-review` and `/stats`, and neither on `/about`. The modal offers
`Reset the database, Continue`, arming to `Confirm, Cancel, Continue`. Cancel returns to the first and
leaves `/health` at `503`. Confirm clears the reason in place, `/health` and all seven reads answer `200`,
and `database.quotes` reads `0`.

**The repair logs `TaskCanceledException` lines, and nothing else.** Since the mark taken after Cancel,
the exception channel read 9 lines, then 11 moments later, every one of them that type: #445. The
malformed upload then answered `422` and logged its `JsonReaderException`, and nothing was thrown after it.

## Canary

Build the commit before [#350](https://github.com/DutchJaFO/Quotinator/issues/350)'s first code
change, under a distinct tag, and run steps 2 to 6 against it.

Step 3 must read `200` and `healthy`. That is #350's own title: a recorded version ahead of the build
ran healthy instead of degrading, against a schema whose shape the application cannot know. With no
degradation there is nothing for steps 5 and 6 to read either, so the reason and the repair route both
fail with it.

**Step 1 is the control.** It leaves a healthy, fully migrated database, so step 3's reading is about
the recorded overshoot rather than about a container that was broken before the version was touched.

Step 6's own repair was changed after #350's first pass, when a repaired page reloaded rather than
being told the state had changed. A pre-fix reading of the reload behaviour is not evidence about the
current step, so run step 6 against the current build only.

Remove the container, image and worktree afterwards.

**Where the canary has been run, 2026-10-31.** Against `e4078444`, the commit before #327 and before #350, as `quotinator:canary-327`: step 3 read `status=healthy` with no overshoot reason. A fuller pass on 2026-10-24 against the same commit read every database read `200`, `404` from `/api/v1/admin/backups`, no overshoot in step 5's modal, and no repair control on any page.

## Cleanup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-06 --since-mark C:\repos\Quotinator\.claude\temp\qt-startup-06-shots\after-control.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-06 --bind C:\repos\Quotinator\.claude\temp\qt-startup-06-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-06-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-06-shots
```

**Expected:** the `log` command exits `0`, nothing thrown since the positive control, before the
container and both directories are removed.
