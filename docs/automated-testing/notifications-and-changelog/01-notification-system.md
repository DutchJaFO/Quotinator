# Notifications list, dismiss, render, and drive their action

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #278
**Fully green after:** [#472](https://github.com/DutchJaFO/Quotinator/issues/472): step 9 reads no notifications until then, because a Reset from a notification's action writes no reseed recommendation. See the [Knowledgebase entry](../../knowledgebase/a-reset-from-a-notification-leaves-no-reseed-recommendation.md)

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-01`), with its data directory bound to a host path so
step 7 can reach the file, and with `Quotinator__IncludeDefaultSources=false` so nothing bundled is in the database. Step 1
imports [a quote](../import-and-staged-actions/fixtures/review-quote.json), the content the Reset in step 8 has to remove,
so a broken import blocks this document.

The Status-filter and Action-button checks additionally need four rows that no producer creates on its own: one
`ActionRequired` row with `DismissTriggerKey = 'DatabaseReset'`, one already-expired row, one already-dismissed row, and one
dismissed with reason `Obsolete`. Nothing has produced an obsolete notification since #372 stopped reseeds truncating
batches, but databases that already hold one still have to read it correctly. Step 7 constructs
[them](fixtures/constructed-notifications.sql) directly in `System_Notification`; this is the index's first case for a
constructed fixture, a state the application cannot be driven into through its own surfaces.

## Determinism

**Never assert a total notification count.** The number on a fresh container changes whenever a producer is added or the
changelog gains a notification-flagged highlight for the running version. Assert the presence of the notification a *known
cause* produces, which is what this test is actually about. The known cause in step 1 is the #279 announcement, which is
written from the changelog rather than from the bundled quote sources.

- **Waits for health, not a duration.**
- **On the home page, notifications are counted by their body cells, not by table rows**, because that page has another table
  with a row of its own.
- The dismiss checks use a **fixed all-zero id** that cannot exist, so the `404` is about the id being absent rather than
  about any particular row's state.
- **Ids are sent in both cases**, per the index: dismissing one notification upper-cased and one lower-cased, whatever form
  the server returned, and the all-zero id is the value that must not match.
- The empty-state check requires dismissing **every** active row first, since a fresh container is not empty, so the
  genuinely-zero-rows path is otherwise never exercised.
- **Step 7 edits the database from the host, against a stopped container.** Writing to a SQLite file under a running process
  is a different scenario, and a clean `docker stop -t 15` leaves no sidecars for SQLite to recover from.
- **The log is read before the stop.** Stopping logs exceptions of its own, so the first read comes before it, and the last
  counts only what was logged since. `capture-page.csx` closes its browser when each command ends, so no page is left to
  reconnect to the restarted container.
- **Quotes are read from `/quotes`, not `/api/v1/version`**, whose counts an import does not refresh (#468). That read is what
  separates Cancel from Confirm, since both leave the page looking similar.

## Steps

### 1. Start a container of this test's own and list its notifications

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-01-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-01-out --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-01-data
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-01 --port 18501 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-01-data --env Quotinator__IncludeDefaultSources=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18501/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\import-and-staged-actions\fixtures\review-quote.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18501/api/v1/notifications?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list.json --contains "items=operation IDs were renamed"
```

**Expected:** `200`, and the announcement about the renamed API operation IDs is present, the notification a fresh container
is known to produce. Asserted by identity, never by a total.

**On failure:** every step below reads this container, so stop.

### 2. Dismiss an unknown notification with no admin key

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18501/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss --no-key --expect 401 --status
```

**Expected:** `401`.

### 3. Dismiss the same all-zero id with the correct key

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18501/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss --expect 404 --status
```

**Expected:** `404`, since no notification exists with that id. Taken with step 2, that separates "the key was rejected" from
"the row was not found"; either alone could be produced by the other cause.

### 4. Confirm the OpenAPI spec carries the Notifications tag

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18501/openapi/v1.json --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\spec.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\spec.json --contains paths./api/v1/notifications.get.tags=Notifications --contains tags=Notifications
```

**Expected:** the list endpoint is grouped under `Notifications`, read from the operation's own `tags` array rather than
matched as text, and the spec's top-level `tags` declares it. That every tag an operation uses is declared with a description
is held at unit tier, by `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription`.

### 5. Render the notification pages

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\notifications.png --script-file scripts/testing/page-scripts/notification-page-state.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\notifications.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\notifications.json --contains title=Notifications --greater-than rows=0 --equals announcementShown=true --equals stackTraceShown=false
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/ --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\home.png --script-file scripts/testing/page-scripts/notification-page-state.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\home.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\home.json --equals readyShown=true --greater-than bodyCells=0 --equals announcementShown=true --equals statsShown=true --equals notificationsHeadingInMain=true --equals stackTraceShown=false
```

**Expected:** `/notifications` lists rows, with the announcement's body among them, since the table renders the message rather
than the title. `/` shows the startup-success wording, the same announcement and the stats. Neither shows a stack trace. The
screenshots are evidence, and the reads are what fail.

### 6. Dismiss every active notification and re-read both pages

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18501/api/v1/notifications/{id}/dismiss" --upper-value id=C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list.json#items.0.id --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\dismiss-upper-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\dismiss-upper-url.txt --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18501/api/v1/notifications/{id}/dismiss" --lower-value id=C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list.json#items.1.id --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\dismiss-lower-url.txt
dotnet script scripts/testing/http.csx -- --method POST --url-file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\dismiss-lower-url.txt --expect 200 --status
dotnet script scripts/testing/dismiss-notifications.csx -- --url http://localhost:18501/api/v1
dotnet script scripts/testing/http.csx -- --url "http://localhost:18501/api/v1/notifications?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-dismissed.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-dismissed.json --greater-than totalCount=0 --equals items.0.isDismissed=true --equals items.1.isDismissed=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\notifications-empty.png --script-file scripts/testing/page-scripts/notification-page-state.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\notifications-empty.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\notifications-empty.json --equals rows=0 --equals emptyFilterShown=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/ --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\home-empty.png --script-file scripts/testing/page-scripts/notification-page-state.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\home-empty.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\home-empty.json --equals readyShown=true --equals bodyCells=0 --equals notificationsHeadingInMain=false --equals statsShown=true
```

**Expected:** the first two dismiss in each case and answer `200`, the script dismisses the rest and reports none still active,
and the list still holds them, flagged: `GET /notifications` returns dismissed rows too, while the *page's* filter defaults to
Active. `/notifications` then shows no rows and *No notifications match this filter.*; `/` drops its Notifications section rather
than leaving an empty heading, and its stats still render.

### 7. Insert the four rows no producer creates

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-01 --pattern "\[Runtime - Exception\]" --exactly 0
docker stop -t 15 qt-notif-01
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-01 --mark C:\repos\Quotinator\.claude\temp\qt-notif-01-out\after-stop.mark
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-notif-01-data\quotinatordata.db --sql-file C:\repos\Quotinator\docs\automated-testing\notifications-and-changelog\fixtures\constructed-notifications.sql
docker start qt-notif-01
dotnet script scripts/testing/http.csx -- --url http://localhost:18501/api/v1/health --wait-for 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18501/api/v1/notifications?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-constructed.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-constructed.json "Smoke test action required" --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-constructed.json "Smoke test expired" --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-constructed.json "Smoke test dismissed" --expect-text C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-constructed.json "Smoke test obsolete"
```

**Expected:** no exception so far, since nothing in steps 1 to 6 threw. `execute-sql.csx` reports `4 row(s) affected`, and all four
rows are readable through the API.

**On failure:** a SQL error means the rows were never created, and every assertion in step 8 then reads an unchanged page. Stop.
A CHECK-constraint rejection is worth reading rather than working around: `Type`, `DismissTriggerKey` and `DismissReason` are
constrained to their enum's current members, so a failure means the enum moved and this fixture needs updating with it.

### 8. Check the status column, the status filter, and the Action button

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\filters.png --script-file scripts/testing/page-scripts/notification-filters.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\filters.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\filters.json --equals activeSelected=true --equals active.rows=1 --equals active.actionRowListed=true --contains "active.actionButtons=Reset the database" --equals all.action=Active --equals all.expired=Expired --equals all.dismissed=Dismissed --equals "all.obsolete=No longer applicable" --equals expiredOnly.rows=1 --equals expiredOnly.expiredRowListed=true
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\cancel.png --script-file scripts/testing/page-scripts/notification-action-cancel.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\cancel.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\cancel.json --contains "before=Reset the database" --contains armed=Confirm --contains armed=Cancel --contains "afterCancel=Reset the database" --length-equals afterCancel=1
dotnet script scripts/testing/http.csx -- --url http://localhost:18501/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\quotes-after-cancel.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\quotes-after-cancel.json --equals totalCount=1
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18501/notifications --out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\confirm.png --script-file scripts/testing/page-scripts/notification-action-confirm.js --result-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\confirm.json --wait-ms 4000
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\confirm.json --equals rowGone=true
dotnet script scripts/testing/http.csx -- --url http://localhost:18501/api/v1/quotes --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\quotes-after-confirm.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\quotes-after-confirm.json --equals totalCount=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18501/api/v1/notifications?pageSize=0" --expect 200 --status --body-out C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-after-confirm.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-no-text C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-after-confirm.json "Smoke test"
```

**Expected, filters:** the page opens on **Active**, listing only the `ActionRequired` row, with its **Reset the database** button.
**All** reads `Active`, `Expired`, `Dismissed` and `No longer applicable` for the four rows. The undismissed row past its `ExpiresAt`
reads `Expired`, never `Active`, which is the computed-status assertion, and the row dismissed as obsolete reads `No longer
applicable`, never `Dismissed`. **Expired only** lists exactly the expired row.

**Expected, Cancel:** arming the action offers **Confirm** and **Cancel**, Cancel restores the single button, and the quote is still
there, since Cancel called nothing.

**Expected, Confirm:** the row leaves the page, no quote remains, and none of the four constructed rows survives, since Reset wipes
`System_Notification` along with every other table.

### 9. The Reset leaves its reseed recommendation

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-01-out\list-after-confirm.json --equals totalCount=1 --equals items.0.metadataKind=reseedrecommended --equals items.0.type=actionrequired --equals items.0.isDismissed=false
```

**Expected:** one active reseed recommendation, the same result `POST /admin/database/reset` leaves in
[`database-lifecycle/03`](../database-lifecycle/03-reset-is-a-full-wipe.md).

**On failure:** `totalCount=0` is [#472](https://github.com/DutchJaFO/Quotinator/issues/472), a known issue described in the
[Knowledgebase](../../knowledgebase/a-reset-from-a-notification-leaves-no-reseed-recommendation.md). Record it and carry on, since
nothing below reads this step. Any other reading is not #472 and is reported in its own right.

### 10. Nothing unexpected was thrown since the restart

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-01-out\after-stop.mark --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18501/api/v1/import --file C:\repos\Quotinator\docs\automated-testing\startup-and-degradation\fixtures\malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-01 --since-mark C:\repos\Quotinator\.claude\temp\qt-notif-01-out\after-stop.mark --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** no exception since the restart, the Reset included. The malformed upload then answers `422` and its
`JsonReaderException` is logged, which is the positive control: the same channel that read zero can see an exception.

**On failure:** a count above zero is an issue, unless a Knowledgebase entry says otherwise. Report it with the line that names it.
A `CryptographicException` / `AntiforgeryValidationException` pair means the browser held a cookie from a container outside the suite's
shared key ring; see [*A cookie from another key ring is replaced on the first page, and logged only
then*](../api-surface/06-a-cookie-from-another-key-ring-is-replaced.md).

## Observed effect

**Measured 2026-11-01** against `quotinator:local` built from `00a9dfe2`, with a browser driven by `capture-page.csx`.

| Stage | Reading |
|---|---|
| First boot, `GET /notifications` | The renamed-operation-ids announcement is present, among 3 rows |
| Dismiss the all-zero id, no key | `401` |
| Dismiss the all-zero id, with the key | `404` |
| Spec | The list operation carries `Notifications`, and the top-level `tags` declares it |
| `/notifications` | 3 rows, with the announcement's body, and no stack trace |
| `/` | *Quotinator is ready*, *Startup completed successfully.*, 3 notifications, the stats, and no stack trace |
| Dismiss one id upper-cased, one lower-cased, and the rest | `200` each, none still active, all 3 still listed |
| `/notifications` after that | No rows, and *No notifications match this filter.* |
| `/` after that | No notifications, no Notifications heading, and the stats still render |
| Filters, with the four constructed rows | Opens on Active with 1 row. All reads `Active`, `Expired`, `Dismissed`, `No longer applicable`. Expired only lists 1 row |
| Cancel | `Confirm` and `Cancel` offered, then `Reset the database` again, and `/quotes` still reads `1` |
| Confirm | The row leaves the page, `/quotes` reads `0`, and none of the constructed rows survives |
| The reseed recommendation after Confirm | **`GET /notifications` lists nothing, where it should list one: #472** |

**No exception was logged before the stop, or after the restart before the malformed upload**, the Reset included. That upload's
`JsonReaderException` was then logged once.

## Canary

Build the commit before [#278](https://github.com/DutchJaFO/Quotinator/issues/278)'s first code
change, under a distinct tag, and run steps 1, 5, 6 and 8 against it. There is no notification system
on that build, so the list endpoint answers `404`, the pages render no rows, and the status column,
filter and Action button have nothing to show.

**Step 4 has a different pre-fix build, and it is this issue's own.** The `Notifications` tag was used
by its operations but never declared with a description until #339 added it, so the commit before that
change publishes a spec where the tag renders bare and last.

Steps 2 and 3 are the control on the #339 build: an unknown id rejected without a key and accepted with one is about the
admin gate rather than about notifications existing.

**Measured 2026-11-01:**

| Build | Steps, in order | Reading |
|---|---|---|
| `quotinator:canary-278`, from `6dd617ef` | 1, 2, 3, 5, 6, 8 | Step 1 fails: `404`. Step 2 fails: `404` rather than `401`. Step 3 reads `404`, which shows nothing there. Step 5 fails: `/notifications` has no title and no rows, and `/` shows the ready wording and stats and no notifications. Step 6 fails: no id to read, `404` on the list, and no empty-filter wording. Step 8 fails: the page scripts throw, having no table to read, and the quote remains |
| `quotinator:canary-339`, from `7a25bd38` | 1, 2, 3, 4 | Steps 1 to 3 pass, `401` then `404`. Step 4 fails: the list operation carries `Notifications`, and the spec's top-level `tags` does not declare it |

**Step 2 does not pass on `canary-278`, and that was tested on its own.** In a separate container of that build, a route
that cannot exist answers the same `404`, a keyless reset answers `401`, so the gate works, and the spec holds no
`notifications` at all while holding the reset path. The route is absent, which is why this is not a control there.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-01 --bind
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-01-data --delete C:\repos\Quotinator\.claude\temp\qt-notif-01-out
```

**Expected:** the container, its data directory and the output directory are gone.
