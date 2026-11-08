# A startup that cannot take a backup loads nothing, and offers only the options that can run

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #348

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-backup-06`, publishing `18386`, on a bind
directory so the test can place a file in `backups/` from the host. The database has its genre rows
removed and the backup quota filled, so the next start has content to load and no room for the backup
that must come first.

A startup loads content into a database that is missing some, and a backup is taken first because that
load writes what nothing else could restore. Until #348 a startup with no room for that backup loaded
nothing and said nothing: the application reported healthy, incomplete, with no explanation and no way
forward. This proves the refusal is reported as a notification, that it offers exactly the options that
can run, that each option works through the page, and that resolving the obstacle by hand brings back
the one option that keeps a restore point.

## Determinism

- **The obstacle is a full budget, made by one sparse file the size of the whole 1 GB default.** The
  backup a startup takes before loading content is measured against the absolute ceiling, not the 90%
  operating quota: filling to 95% leaves it room in the reserve, and the content loads (measured). At the
  ceiling it refuses with `BudgetExceeded`, and the notification's own check, which does use the
  operating quota, agrees. A sparse file allocates without writing a gigabyte.
- **The filler is made the oldest backup.** Its write time is set to 2020, before any backup a cycle
  leaves behind. That makes removing the oldest backup enough to clear the quota, so *Remove the oldest
  backup, then back up and reseed* is offered; left newest, removing the oldest would free only a few
  megabytes and the option would rightly be withheld.
- **The pending load is the genre re-seed, and it is produced by removing the genre rows.** Since
  [#423](https://github.com/DutchJaFO/Quotinator/issues/423) a start only loads the configured files into
  a database it created itself, so an emptied quote table no longer gives a restart anything to load.
  Quotes present with genres absent is the one state in which a start that did not create the database
  still loads content, which is `ReSeedGenresIfEmptyAsync`. It is reached by editing the database: the
  application has no operation that produces it, and that is the point of #423 rather than a gap in it.
  Rows, not schema, are removed, so the integrity check (#305) has nothing to report.
- **The genre count is the evidence of a load having run or been refused.** No endpoint reports it, so
  DbInspector reads it from the file, read-only, and writes it as JSON (`--json-out`) for TestProbe.
- **The edit is made while the container is stopped.** `docker stop -t 15` lets SQLite checkpoint and leave
  no `-wal`/`-shm` sidecar behind; writing to the file underneath a running container would be writing to a
  database mid-checkpoint. So each cycle stops, edits, then re-enters.
- **Each option is proven in its own cycle.** An option that succeeds loads content and resolves the
  notification, so each of the three is reached by repeating the refusing cycle: remove the genre rows,
  fill the budget, restart. Steps 2, 6 and 7 each run it, written out in full.
- **The notification is read by its kind, never by position or total.** How many notifications exist
  depends on producers this test is not about, and rows written in the same second have no defined order
  (#486). [`notification-of-kind.js`](../../../scripts/testing/page-scripts/notification-of-kind.js) reads it.
- **The page is driven through the DOM, and a click is retried until Confirm appears.**
  [`notification-option-run.js`](../../../scripts/testing/page-scripts/notification-option-run.js) does
  this, since controls need the Blazor circuit and a click issued before it connects is swallowed, then
  waits, at most 120 seconds, for the page's running spinner to appear and go (#367). The results asserted
  afterwards are read once the action has finished.
- **The backup count is exact, derived from what each step leaves.** The filler matches `*.db` too: one file
  after step 2, still one after step 5, which takes no backup, one after step 6, which removes the filler
  and takes one backup, and two after step 7, whose cycle adds a filler beside that backup, whose manual
  removal takes it away again, and whose option takes one more.
- **The startup popup renders once per process run**, so it is read before the Notifications page.
- **The log is read before each stop.**

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-06 --delete C:\repos\Quotinator\.claude\temp\qt-backup-06-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-06\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-06-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-06 --port 18386 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-06
dotnet script scripts/testing/http.csx -- --url "http://localhost:18386/api/v1/quotes?pageSize=1" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\quotes-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\quotes-seeded.json --greater-than totalCount=0
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-seeded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-seeded.json --greater-than 0.Genres=0
```

**Expected:** the environment reports healthy, and both `json` commands exit `0`: quotes above `0`, and
genres above `0`.

**This is the positive control for the whole document.** With room for a backup, a startup loads content;
everything below asserts what happens without that room.

### 2. Run the refusing cycle, and confirm the content load was refused without degrading

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-06 --pattern "\[Runtime - Exception\]" --exactly 0
docker stop -t 15 qt-backup-06
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "DELETE FROM Quotinator_QuoteGenre;"
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db --sparse C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db 1073741824 --set-write-time C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db 2020-01-01
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-06 --port 18386 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-06
dotnet script scripts/testing/http.csx -- --url http://localhost:18386/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18386/api/v1/quotes?pageSize=1" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\quotes-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\quotes-refused.json --greater-than totalCount=0
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-refused.json --equals 0.Genres=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-06 --pattern "seeding refused: no backup could be taken \(BudgetExceeded\)" --at-least 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-06\backups "*.db" 1
```

**Expected:** every command exits `0`: nothing thrown before the stop, the restart healthy with `/health`
`200`, quotes still above `0`, genres `0`, the refusal logged naming `BudgetExceeded`, and the filler the
only file in `backups/`.

**On failure:** genres above `0` means the content loaded without its backup: the refusal this whole
document is about did not happen. A health other than `200` means the refusal degraded the application,
which a content-load refusal must not do; the schema is intact. Quotes at `0` means the wipe removed more
than the genre rows, and every assertion below reads a state this document never set up.

### 3. Confirm the refusal is a notification offering exactly the options that can run

```
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#backuprefused" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused.png --script-file scripts/testing/page-scripts/notification-of-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused.json --equals active=1 --equals type=actionrequired --equals dismissTriggerKey=reseed --equals availableActions=removeoldestbackupthenreseed,reseedwithoutbackup
```

**Expected:** the `json` command exits `0`: one active `actionrequired` notification, cleared by a reseed,
offering exactly `removeoldestbackupthenreseed,reseedwithoutbackup`.

**The absence matters as much as the presence.** `backupthenreseed` is not offered because no backup can
be taken; listing it would offer an option that then refuses, which is the defect #348 was reopened for.
The exact list asserts both halves.

**On failure:** `active=0` with step 2 passing is the pre-#348 behaviour: refused, and silent.

### 4. Confirm both surfaces render the options and the Knowledgebase link

```
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/#Content was not loaded" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\popup.png --height 1100 --script-file scripts/testing/page-scripts/notification-row-by-text.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\popup.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\popup.json --equals found=true --greater-than innerHeight=0 --equals buttonCount=0 --contains links=docs/knowledgebase/no-backup-could-be-taken.md
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#Content was not loaded" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\page.png --height 1100 --script-file scripts/testing/page-scripts/notification-row-by-text.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\page.json --equals found=true --greater-than innerHeight=0 --equals "buttons=Remove the oldest backup, then back up and reseed | Reseed without a backup | Dismiss" --contains links=docs/knowledgebase/no-backup-could-be-taken.md
```

**Expected:** both `json` commands exit `0`. Both surfaces find the row with a non-zero viewport and link the
Knowledgebase entry `no-backup-could-be-taken.md`. The popup is read-only, so it carries no button; the
page's buttons are exactly the two options and `Dismiss`, with no `Back up, then reseed`. `popup.png` and
`page.png` show the row.

### 5. Reseed without a backup, through the page: asked first, then run and recorded

```
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#Content was not loaded|Reseed without a backup" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\without-backup.png --script-file scripts/testing/page-scripts/notification-option-run.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\without-backup.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\without-backup.json --equals confirmOffered=true --contains asked=BudgetExceeded --greater-than askedLinks=0 --equals sawRunning=true --equals finished=true
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-without-backup.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-without-backup.json --greater-than 0.Genres=0
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#backuprefused" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-after-without.png --script-file scripts/testing/page-scripts/notification-of-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-after-without.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-after-without.json --equals active=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-06\backups "*.db" 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-06 --pattern "reseed proceeding WITHOUT a backup \(BudgetExceeded\)" --at-least 1
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#Database" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\audit-without.png --script-file scripts/testing/page-scripts/audit-operations.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\audit-without.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\audit-without.json --equals status=200 --equals operations.BackupSkipped=1
```

**Expected:** every command exits `0`. Before anything ran, the row asked, naming `BudgetExceeded`, with the
Knowledgebase link beside it; then the action ran and finished. Genres are back above `0`, the refusal is
no longer active, no backup was added (the filler is still the only file), the skip is logged, and one
`BackupSkipped` entry is in the audit trail.

**On failure:** a reseed that ran without asking, naming the obstacle, took the user's permission without
telling them what they were agreeing to.

### 6. Remove the oldest backup, then back up and reseed, through the page

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-06 --pattern "\[Runtime - Exception\]" --exactly 0
docker stop -t 15 qt-backup-06
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "DELETE FROM Quotinator_QuoteGenre;"
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db --sparse C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db 1073741824 --set-write-time C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db 2020-01-01
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-06 --port 18386 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-06
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#backuprefused" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-again.png --script-file scripts/testing/page-scripts/notification-of-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-again.json --equals active=1
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-refused-again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-refused-again.json --equals 0.Genres=0
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#Content was not loaded|Remove the oldest backup, then back up and reseed" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\remove-oldest.png --script-file scripts/testing/page-scripts/notification-option-run.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\remove-oldest.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\remove-oldest.json --equals confirmOffered=true --equals sawRunning=true --equals finished=true
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-06\backups "*.db" 1
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-remove-oldest.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-remove-oldest.json --greater-than 0.Genres=0
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#Database" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\audit-remove.png --script-file scripts/testing/page-scripts/audit-operations.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\audit-remove.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\audit-remove.json --equals operations.BackupDeleted=1 --equals operations.BackedUp=1
```

**Expected:** every command exits `0`. The cycle refuses again (one active refusal, genres `0`); then the
option runs and finishes, the filler is gone and one backup stands in its place, genres are back above
`0`, and the audit trail holds one `BackupDeleted` and one `BackedUp`.

**On failure:** the filler gone with genres still `0` means the backup was removed and nothing was loaded:
a restore point given up for nothing.

### 7. Resolve the obstacle by hand, and confirm backing up first is offered and works

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-06 --pattern "\[Runtime - Exception\]" --exactly 0
docker stop -t 15 qt-backup-06
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "DELETE FROM Quotinator_QuoteGenre;"
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db --sparse C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db 1073741824 --set-write-time C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db 2020-01-01
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-06 --port 18386 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-06
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-06\backups\filler.db
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#backuprefused" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-resolved.png --script-file scripts/testing/page-scripts/notification-of-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-resolved.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\refused-resolved.json --equals active=1 --equals availableActions=backupthenreseed
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18386/notifications#Content was not loaded|Back up, then reseed" --out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\back-up-first.png --script-file scripts/testing/page-scripts/notification-option-run.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\back-up-first.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\back-up-first.json --equals confirmOffered=true --equals sawRunning=true --equals finished=true
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-06\backups "*.db" 2
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-06\quotinatordata.db --sql "SELECT COUNT(*) AS Genres FROM Quotinator_QuoteGenre" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-back-up-first.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-06-out\genres-back-up-first.json --greater-than 0.Genres=0
```

**Expected:** every command exits `0`. With the filler removed by hand, the one active refusal now offers
only `backupthenreseed`, the option that keeps a restore point; it runs and finishes, adds one backup beside
step 6's, and genres are back above `0`.

**This step is the remedy proven.** The Knowledgebase entry tells an operator with a full quota to remove
old backups; done by hand here, it brings back the option that keeps a restore point, and that option
loads the content.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `01ab4ef6`, in this form and in the
PowerShell form it replaced, with the same readings.

The seeded container reports `795` quotes and `25` genres. With the genre rows removed and the budget
full, the next start loads nothing and stays healthy (`/health` `200`, quotes unchanged, genres `0`), and
logs `seeding refused: no backup could be taken (BudgetExceeded)`. One active `actionrequired`
notification, *Content was not loaded: no backup could be taken*, cleared by a reseed, offers
`removeoldestbackupthenreseed,reseedwithoutbackup`, and the page renders exactly those two as buttons,
plus `Dismiss`, with the Knowledgebase link beside them; the read-only startup popup renders the row and
the link with no button.

*Reseed without a backup* first shows *No backup can be taken right now (BudgetExceeded), so this reseed
will leave no restore point…* with the link, then reseeds the genres, writes no backup, logs `reseed
proceeding WITHOUT a backup (BudgetExceeded)` and records one `BackupSkipped` audit entry. *Remove the
oldest backup, then back up and reseed* removes the filler, takes one backup and reseeds; both are audited.
With the filler removed by hand, only *Back up, then reseed* is offered, and it writes one backup and
reseeds. No `[Runtime - Exception]` line at any stop.

**The earlier runs, 2026-09-26 and 2026-10-10**, measured the same behaviour, the first through the
Reset-based setup #423 since retired. What follows was found on those first runs and is kept because each
item is still held by a test.

**The first runs found two application defects and three in this document.**

The application logged the obstacle quoted, `("BudgetExceeded")`: Serilog quotes a string property
unless the template marks it literal, and the unit tests read it through a logger that does not, so they
passed. All five lines naming an obstacle now use `{Obstacle:l}`, held by
`BackupObstacleLogRenderingTests`, which renders through Serilog.

And a reseed resolved its notification while it was still running. Seeding applies one batch per file,
and each successful batch dismissed every `Reseed`-trigger notification, so the notification read as
resolved after the first file, with `13` of `795` quotes loaded and the reseed still going. A
notification is updated after the action that settles it has run, never during it: a seed run no longer
dismisses anything, and its owner does once the run has returned (the notification's own action, the
admin reseed endpoint, and a startup that loaded content). Held by
`DatabaseInitializerTests.ReseedAsync_LeavesTheRefusalForItsCallerToResolve` and
`InitialiseAsync_ContentLoadedAfterAnEarlierRefusal_ResolvesTheRefusal`, whose fixture had been building
the import path with a notification writer that did nothing, which is why no unit test had seen it.

The document's own faults then were a one-element list read without `@(...)`, a resolved row waited on
after it had left the view, and a leftover filler that made the next cycle's own Reset refuse.

**Also measured, and recorded rather than asserted: a startup's content-load backup is measured against
the absolute ceiling, not the 90% operating quota.** A 95% filler leaves it room in the reserve, and the
content loads. The notification's options consult the operating quota, so the two can disagree between
90% and 100%. This document fills to the ceiling, where both refuse.

## Canary

Run 2026-09-26 against `4cfe1006`, the commit before step 8 of #348's plan, as `quotinator:canary348`,
through steps 4 (steps 5 to 7 click controls that build does not have).

**This canary ran the Reset-based setup this document no longer uses** (#423). Its three substantive
reds are about reporting, not about how the refusal was reached, so they hold as written; the row below
that reads `quotes=0` is the one whose evidence the re-base changes. Re-running the canary needs an
image built from `4cfe1006` and has not been done since.

| Step | Assertion | Canary |
|---|---|---|
| 2 | the content load is refused | passes, on that build's own Reset-based setup: `quotes=0` |
| 2 | the refusal is logged | **fails**: that build words the line `seeding refused — no backup could be taken`, since reworded |
| 3 | one `backuprefused` notification | **fails**: `found=0` |
| 3 | it offers exactly the options that can run | **fails**: nothing to offer them |
| 4 | both surfaces render the row and the Knowledgebase link | **fails**: `found=False` on both |

Step 3 is the substantive red: that build refused the load and told nobody, the defect this document
exists to catch. Step 2's log line fails there on wording alone.

## Cleanup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-06 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-06 --bind C:\repos\Quotinator\.claude\temp\qt-backup-06
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-06 --delete C:\repos\Quotinator\.claude\temp\qt-backup-06-out
```

**Expected:** the `log` command exits `0`, and the container and both directories are gone.
