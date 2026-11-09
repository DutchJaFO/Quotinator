# Every notification kind is produced by its own trigger and renders what its layout promises

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #308

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-notif-14`, publishing `19514`, created
with `--env Quotinator__AdminApiKey=t2-308` and a bind directory holding the shared conflict fixture in
its `imports/` folder. It is restarted three times and reset once; each of those is a trigger, not
scaffolding.

#308 defines a per-type layout across both surfaces. This asserts both halves for every kind: that the
kind's own trigger produces it, and that what a reader then sees matches the decision.

## Determinism

**Each kind arrives through its real trigger, not a constructed row.** A constructed row proves
rendering and nothing about the producer, and a producer that stopped writing would leave every
rendering assertion passing over a page nobody could reach in practice:

| Kind | Its trigger here |
|---|---|
| `Announcement` | a cold start on a fresh database |
| `WhatsNew` | the same cold start, from the bundled changelog |
| `ReseedFileApplied` | the same cold start, one per bundled file that applied cleanly |
| `ImportReviewPending` | the conflict fixture in `imports/`, staged by that seed |
| `DatabaseCreated` | the same cold start, which is what creates the database (#371) |
| `SchemaVersionOvershoot` | a consumer schema version rolled one past this build, then a restart |
| `ReseedRecommended` | a database reset, which leaves the database empty |

**`BackupRefused`, `BackupQuotaReached`, `BackupMaxExceeded`, `MigrationApplied` and
`UnreadableDatabaseSetAside` are not produced here.** Each needs a state no other kind here shares: a
startup that cannot take a backup loads no content
([`../backup/06`](../backup/06-a-startup-that-cannot-take-a-backup-loads-nothing.md)), a backups folder
past its quota or its maximum ([`../backup/08`](../backup/08-backups-continue-from-the-reserve-until-the-ceiling-refuses-one.md)),
an upgrade from an earlier build's database
([`16`](16-each-applied-migration-says-what-it-did.md)), and a Reset of a database file that cannot be
read ([`../backup/01`](../backup/01-a-reset-sets-aside-a-database-it-cannot-read.md)). Each of the first
four documents produces its kind and renders it on both surfaces; `backup/01` produces its kind and reads
it from the API, and its page rendering is the same no-detail, `Dismiss`-only row `DatabaseCreated` has,
asserted by `NotificationTableTests`. Every cold start here takes the baseline path, which applies no individual
migration, so `migrationapplied` must be absent, and the exact kind list asserts that it is.

**The overshoot's trigger is a version row, not a notification row.** Inserting `MAX(Version) + 1` into
`System_ConsumerSchemaVersion` is what
[`../startup-and-degradation/06`](../startup-and-degradation/06-schema-version-ahead-of-the-application.md)
uses, and the producer writes the notification. The row is then removed again, because while the version
stands ahead the application is degraded and shows nothing read from the database (#350): the
notification survives, the degradation does not.

**The reset comes last but one.** Reset rebuilds every table (#156), so it takes the other kinds with it,
and nothing after it may assume they are still there.

**The detail expectation follows the payload.** A kind opens detail when its payload carries a non-empty
`counts` array: `reseedfileapplied` and `importreviewpending`. Every other kind here carries none, and its
body already says what its payload holds.

**`NotificationMetadataKind` is the source of the case list.** A kind added later must fail until it is
covered here; `NotificationTableTests.EveryLiveNotificationKind_IsNamedInTheVariantDocument` asserts
this document names every member.

**Every reading is a committed page script run by `capture-page.csx`**, which loads the page, runs the
script, prints and saves what it returns, and writes the PNG, so the assertion and the image describe the
same moment. A script returning a rectangle crops the image to it:
[`notification-row.js`](../../../scripts/testing/page-scripts/notification-row.js) finds the row whose
title holds the text in the URL fragment (`#needs review`, or `#needs review|expand` to open its in-place
detail first). [`notification-kinds.js`](../../../scripts/testing/page-scripts/notification-kinds.js)
reads the kinds from the API, and
[`notification-rows-by-kind.js`](../../../scripts/testing/page-scripts/notification-rows-by-kind.js)
pairs each rendered row with its kind, on the page or in the startup popup. Images land in
`.claude/temp/qt-notif-14/`, gitignored: this run's evidence, produced again by the next.

**Nothing passes or fails on an image.** The image records what an assertion cannot describe (spacing,
wrapping, whether a body that reads correctly also looks like one); the scripts' results are what fail.

**Every zero exception count is read from a mark taken after the last restart**, so a stop's own shutdown
lines are behind it, and the last is given its positive control: the malformed upload's
`JsonReaderException`, logged by the same channel.

## Steps

### 1. Cold start with a conflict waiting, and confirm five kinds were produced

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-14-bind --delete C:\repos\Quotinator\.claude\temp\qt-notif-14 --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-14
dotnet script scripts/testing/stage-import-conflict.csx -- --imports C:\repos\Quotinator\.claude\temp\qt-notif-14-bind\imports
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-14 --port 19514 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-14-bind --env Quotinator__AdminApiKey=t2-308
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-first-start.png --script-file scripts/testing/page-scripts/notification-kinds.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-first-start.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-first-start.json --equals "kindList=announcement=1 databasecreated=1 importreviewpending=1 reseedfileapplied=5 whatsnew=1"
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** the conflict is staged, the container reaches healthy, the `json` command exits `0` on
exactly five kinds, and no exception has been logged. `migrationapplied` is absent, as the baseline path
requires.

**On failure:** a missing kind is a producer defect, not a rendering one, and every later step would assert
rendering for a row nobody can obtain. Stop and report which trigger produced nothing.

### 2. Read the page: what each kind shows

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-14\page.png --width 1280 --height 1400 --full-page --script-file scripts/testing/page-scripts/notification-rows-by-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\page-by-kind.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\page-by-kind.json --equals rows=9 --equals byKind.reseedfileapplied.rows=5 --equals byKind.reseedfileapplied.opensDetail=5 --equals byKind.importreviewpending.opensDetail=1 --equals byKind.importreviewpending.extraButtons=Decide --equals byKind.announcement.opensDetail=0 --equals byKind.whatsnew.opensDetail=0 --equals byKind.databasecreated.opensDetail=0 --equals byKind.reseedfileapplied.expandsInPlace=0 --equals byKind.importreviewpending.expandsInPlace=0 --equals byKind.reseedfileapplied.emptyBody=0 --equals byKind.reseedfileapplied.titleInsideBody=0 --equals byKind.importreviewpending.emptyBody=0 --equals byKind.importreviewpending.titleInsideBody=0 --equals byKind.announcement.emptyBody=0 --equals byKind.announcement.titleInsideBody=0 --equals byKind.whatsnew.emptyBody=0 --equals byKind.whatsnew.titleInsideBody=0 --equals byKind.databasecreated.emptyBody=0 --equals byKind.databasecreated.titleInsideBody=0
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#operation IDs" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\announcement-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\announcement-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\announcement-page.json --equals buttons=Dismiss
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#new (unreleased)" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\whatsnew-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\whatsnew-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\whatsnew-page.json --equals buttons=Dismiss
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#Database created" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\databasecreated-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\databasecreated-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\databasecreated-page.json --equals buttons=Dismiss
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#reseeded cleanly" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedfileapplied-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedfileapplied-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedfileapplied-page.json --equals "buttons=Details, Dismiss"
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#needs review" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\importreviewpending-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\importreviewpending-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\importreviewpending-page.json --equals "buttons=Details, Decide, Dismiss"
```

**Expected:** every `json` command exits `0`. Nine rows; every row renders a non-empty body with its title
as its own element, never inside the body; detail opens in a dialog for exactly the five reseeded files and
the review row, and for none of the announcement, what's-new and database-created rows; nothing expands in
place on the page; and the review row alone carries an extra button, `Decide`. The per-row captures read
`Dismiss` alone for the three kinds without detail, `Details, Dismiss` for a reseeded file, and
`Details, Decide, Dismiss` for the review row.

**That is the per-type layout decision, stated as what a reader sees**, and it is asserted both ways: the
kinds whose payload carries counts open detail, and the kinds whose payload does not, do not.

### 3. Open a detail dialog and confirm it describes itself

```
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#reseeded cleanly" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedfileapplied-page-detail.png --width 1280 --height 1000 --script-file scripts/testing/page-scripts/notification-detail-dialog.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\detail.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\detail.json --equals "title=Source file reseeded cleanly" --greater-than headers=0 --greater-than bodyRows=0 --equals footRows=1 --equals everyRowMatchesHeaders=true
```

**Expected:** the `json` command exits `0`: a reseeded file's dialog, with headings, at least one body row,
one **Total** footer row, and every row with one cell per heading.

**The dialog is chosen by its row's title, never by position.** Rows written in the same second are listed
in no fixed order. Found while converting this document: its earlier form opened the first detail control on
the page, which was a reseeded file on one run and the review row on the next, whose breakdown has no total
line.

**Assert the shape, not the column list**: the counts a breakdown carries have changed twice since #308
(#374, #377), and naming them here would fail on the next addition while the behaviour is correct.

### 4. Trigger the overshoot, and confirm its kind is produced and rendered

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --pattern "\[Runtime - Exception\]" --exactly 0
docker stop -t 15 qt-notif-14
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-notif-14-bind\quotinatordata.db --sql "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) SELECT MAX(Version) + 1, '2026-01-01 00:00:00' FROM System_ConsumerSchemaVersion;"
docker start qt-notif-14
dotnet script scripts/testing/http.csx -- --url http://localhost:19514/api/v1/health --wait-for 503 --status
docker stop -t 15 qt-notif-14
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-notif-14-bind\quotinatordata.db --sql "DELETE FROM System_ConsumerSchemaVersion WHERE Version = (SELECT MAX(Version) FROM System_ConsumerSchemaVersion);"
docker start qt-notif-14
dotnet script scripts/testing/http.csx -- --url http://localhost:19514/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-overshoot.mark
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-overshoot.png --script-file scripts/testing/page-scripts/notification-kinds.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-overshoot.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-overshoot.json --equals "kindList=announcement=1 databasecreated=1 importreviewpending=1 reseedfileapplied=5 schemaversionovershoot=1 whatsnew=1"
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#schema version" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\schemaversionovershoot-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\schemaversionovershoot-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\schemaversionovershoot-page.json --equals opensDetail=false --equals "buttons=Reset the database, Dismiss"
```

**Expected:** nothing thrown before the first stop; `OK — 1 row(s) affected.` twice; `503` while the version
stands ahead, then `200`; the earlier five kinds plus `schemaversionovershoot=1`; and its row opens no
detail (its payload carries version numbers its body already states), with one extra button, `Reset the
database`.

**On failure:** no overshoot row means the producer did not run; the version bump is its only trigger, so
check the insert reported a row before reading anything into the rendering.

### 5. Read the startup popup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-overshoot.mark --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-notif-14
dotnet script scripts/testing/http.csx -- --url http://localhost:19514/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-restart.mark
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/ --out C:\repos\Quotinator\.claude\temp\qt-notif-14\popup.png --width 1000 --height 1200 --script-file scripts/testing/page-scripts/notification-rows-by-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\popup-by-kind.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\popup-by-kind.json --equals rows=10 --equals anyButton=false --equals byKind.reseedfileapplied.expandsInPlace=5 --equals byKind.importreviewpending.expandsInPlace=1 --equals byKind.announcement.expandsInPlace=0 --equals byKind.whatsnew.expandsInPlace=0 --equals byKind.databasecreated.expandsInPlace=0 --equals byKind.schemaversionovershoot.expandsInPlace=0 --equals byKind.reseedfileapplied.opensDetail=0 --equals byKind.importreviewpending.opensDetail=0
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/#reseeded cleanly|expand" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedfileapplied-popup-detail.png --width 1000 --height 1400 --script-file scripts/testing/page-scripts/notification-row.js
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/#needs review|expand" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\importreviewpending-popup-detail.png --width 1000 --height 1400 --script-file scripts/testing/page-scripts/notification-row.js
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/#schema version" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\schemaversionovershoot-popup.png --width 1000 --height 1400 --script-file scripts/testing/page-scripts/notification-row.js
```

**Expected:** nothing thrown since the overshoot was cleared; the `json` command exits `0`: ten rows (step
4's ten notifications), no button on any of them, and detail expanding in place for exactly the five
reseeded files and the review row, with no dialog anywhere. That surface difference is what #308 settled:
a dialog on the page, an expander in the popup. The three captures record two rows expanded and the
overshoot as it reads there.

**The popup offers no controls at all: no action button and no Dismiss.** `NotificationSummary` passes
neither `ShowActionColumn` nor `ShowDismissAction`, so every row there is read-only, and `anyButton=false`
is what asserts that absence: a row that grew a button in the popup would be a surface difference nobody
decided.

**The popup is per browser session, not per application run**, so each capture is a session of its own and
none needs a restart. The restart above is what produces this step's mark.

### 6. Confirm a body's line breaks survive

The what's-new body carries one line per changelog highlight.

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/ --out C:\repos\Quotinator\.claude\temp\qt-notif-14\line-breaks.png --width 1000 --height 1200 --script-file scripts/testing/page-scripts/notification-line-breaks.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\line-breaks.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\line-breaks.json --equals whiteSpace=pre-line --equals breaksHonoured=true
```

**Expected:** the `json` command exits `0`: the body's rule is `pre-line`, and it renders more line boxes
under that rule than under `normal`, which collapses the breaks.

**This is why no per-kind line-break flag is needed:** the rule applies to every body, so a kind's layout
has nothing to declare about it.

### 7. Reset, and confirm its kind is produced and rendered

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:19514/api/v1/admin/database/reset?allowNoBackup=true" --api-key t2-308 --expect 200 --status
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-reset.png --script-file scripts/testing/page-scripts/notification-kinds.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-reset.json --equals kindList=reseedrecommended=1
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#no quotes" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedrecommended-page.png --script-file scripts/testing/page-scripts/notification-row.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedrecommended-page.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedrecommended-page.json --equals opensDetail=false --equals "buttons=Back up, then reseed, Dismiss"
```

**Expected:** nothing thrown since the restart; the reset answers `200`; the reset's recommendation is the
only notification left, since the reset took every other with it; and its row opens no detail and offers
one extra button, `Back up, then reseed`: the option's own name, the only one offered, since a backup can
be taken here (#348).

**On failure:** no recommendation means the reset's producer did not run, which is #304's behaviour rather
than this issue's rendering.

### 8. Render that kind in the popup, after a restart that must not reseed it away

A start loads the configured files only into a database it created itself (#423), so this restart leaves
the database as the reset left it, and the recommendation stays active.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
docker restart qt-notif-14
dotnet script scripts/testing/http.csx -- --url http://localhost:19514/api/v1/health --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-second-restart.mark
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/notifications#whatsnew" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-final.png --script-file scripts/testing/page-scripts/notification-kinds.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-final.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\kinds-final.json --equals "kindList=announcement=1 reseedrecommended=1 whatsnew=1" --equals dismissedList=none
dotnet script scripts/testing/http.csx -- --url "http://localhost:19514/api/v1/quotes?pageSize=1" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-14\quotes.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\quotes.json --equals totalCount=0
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:19514/ --out C:\repos\Quotinator\.claude\temp\qt-notif-14\popup-final.png --width 1000 --height 1200 --script-file scripts/testing/page-scripts/notification-rows-by-kind.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-14\popup-final.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-14\popup-final.json --equals anyButton=false --equals byKind.reseedrecommended.rows=1 --equals byKind.reseedrecommended.expandsInPlace=0 --equals byKind.reseedrecommended.emptyBody=0
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:19514/#no quotes" --out C:\repos\Quotinator\.claude\temp\qt-notif-14\reseedrecommended-popup.png --width 1000 --height 1400 --script-file scripts/testing/page-scripts/notification-row.js
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-second-restart.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19514/api/v1/import --api-key t2-308 --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-14 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-14\after-second-restart.mark --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** every command exits `0`. The kinds read `announcement`, `reseedrecommended` and `whatsnew`,
none dismissed; the database holds no quotes; the popup renders the recommendation with a body, no
expander and no button; nothing was thrown since the restart; and the malformed upload answers `422` with
its `JsonReaderException` logged, the positive control for every zero count in this document.

**`quotes=0` is what makes the rest of this step mean anything.** A restart that re-seeded would both refill
the database and resolve the recommendation, so an active recommendation alongside a non-zero count would
mean the listing was read before the producer dismissed it, not that the reset survived.

**The kinds read waits for the what's-new row** (`#whatsnew`): its producer runs detached after startup,
so a listing taken the moment health answers `200` can precede it.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `ace79e6b`.

| Kind | Trigger | Page | Popup |
|---|---|---|---|
| `Announcement` | cold start | no detail, `Dismiss` only | no detail, no button |
| `WhatsNew` | cold start | no detail, `Dismiss` only | no detail; its body renders 6 line boxes under `pre-line` against 5 under `normal` |
| `DatabaseCreated` | cold start | no detail, `Dismiss` only | no detail, no button |
| `ReseedFileApplied` | cold start, five bundled files | detail dialog: 8 headings, 2 rows and a `Total` row, every row one cell per heading; `Details, Dismiss` | expands in place, no button |
| `ImportReviewPending` | the conflict fixture | detail dialog; `Details, Decide, Dismiss` | expands in place, no button |
| `SchemaVersionOvershoot` | a consumer version one past the build | no detail; `Reset the database, Dismiss` | no detail, no button |
| `ReseedRecommended` | a database reset | no detail; `Back up, then reseed, Dismiss` | after a restart, still active with `quotes=0`; no detail, no button |

The page held 9 rows after the first start and the popup 10 after the overshoot; no body was empty and no
title appeared inside its body. Health read `503` while the version stood ahead and `200` once it was
removed. No exception was logged at any read; the malformed upload's `JsonReaderException` was logged once.

**Run as written before this conversion, the PowerShell form read the same**, apart from two readings that
were its own: its popup expectation of `9` rows predated `DatabaseCreated`, and four `WebSocketException`s
followed a capture of a snippet returning an array, which `capture-page.csx` cannot crop to and so exited
without closing its browser.

## Canary

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Built from
`69c01776` (the commit before #308's first feature commit) as `quotinator:canary308c` via
`git worktree add` and `docker build`, 2026-09-23, and read with this document's earlier PowerShell form:

| Step | Assertion | Pre-work result |
|---|---|---|
| 1 | the kinds from the cold start | **fails**, three: `announcement`, `importreviewpending`, `whatsnew` |
| 2 | each row has a `.notification-body` cell | **fails**: `0` of `3` rows; the class does not exist |
| 2 | a title element distinct from the body | **fails**: `0` title elements |
| 2 | a detail control for the kinds carrying counts | **fails**: `0` dialogs and `0` expanders, for any kind |
| 2 | the review row's button names its action | **fails**: the only labels are `Run` and `Dismiss` |

Every per-kind assertion depends on markup that build does not emit, so the document cannot pass there
by accident. Container, image, bind directory and worktree removed afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-14 --bind C:\repos\Quotinator\.claude\temp\qt-notif-14-bind
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-14-bind --delete C:\repos\Quotinator\.claude\temp\qt-notif-14
```

**Expected:** the container and both directories are gone.
