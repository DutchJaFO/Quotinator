# Notifications list, dismiss, render, and drive their action

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #278

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-01` / `qt-notif-01-data`),
because the Action-button check runs a Reset that wipes the database it is started against.
Seeding must be allowed to finish before anything is asserted — the notification a fresh container
produces is written during startup, so an early read cannot tell "not produced" from "not yet".

The Status-filter and Action-button checks additionally need three rows that no producer creates on its
own — one `ActionRequired` row with `DismissTriggerKey = 'DatabaseReset'`, one already-expired row, and
one already-dismissed row. Step 7 constructs them directly in `System_Notification`; this is the index's
first case for a constructed fixture, a state the application cannot be driven into through its own
surfaces.

## Determinism

**Never assert a total notification count.** The number on a fresh container changes whenever a
producer is added or the bundled changelog gains a notification-flagged highlight for the running
version. **This expectation has already been wrong twice** — `0` before #312's producers existed, then
`1` until the unreleased changelog carried a `notification` audience highlight.

Assert the presence of the notification a *known cause* produces, which is what this test is actually
about. A count asserts something nobody intended and gets "fixed" by editing a digit.

- **Waits for health, not a duration.**
- The dismiss checks use a **fixed all-zero id** that cannot exist, so the `404` is about the id being
  absent rather than about any particular row's state.
- The empty-state check requires dismissing **every** active row first — a fresh container is no longer
  empty, so the genuinely-zero-rows path is otherwise never exercised. It produces more than one
  notification now, so dismissing only the announcement leaves the page populated and the empty state
  unreached.
- **Step 7 edits the database from the host, against a stopped container.** A clean `docker stop -t 15`
  checkpoints and removes the `-wal`/`-shm` sidecars (measured), so copying the `.db` file out, editing
  it and copying it back cannot leave a stale sidecar behind for SQLite to recover from.

## Steps

### 1. Start a container of this test's own and list its notifications

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-01 --port 18501

$notifications = (dotnet script scripts/testing/http.csx -- --url "http://localhost:18501/api/v1/notifications?pageSize=0" --expect 200 | ConvertFrom-Json).items
@($notifications | Where-Object { $_.title -match 'operation IDs were renamed' }).Count
```

**Expected:** `200`, and `1` — the announcement about the renamed API operation IDs is present, the
notification a fresh container is known to produce. Asserted by identity, never by a total: see
Determinism for the two occasions a count was wrong here.

**On failure:** an empty list means seeding had not finished writing the startup notification, not that
no notification is produced — the two are indistinguishable from an early read (see Preconditions).
Stop and let the container finish rather than reading the absence as a result.

### 2. Dismiss an unknown notification with no admin key

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18501/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss" `
  --no-key --expect 401 --status
```

**Expected:** `401`.

### 3. Dismiss the same all-zero id with the correct key

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18501/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss" `
  --expect 404 --status
```

**Expected:** `404` — no notification exists with that id. Taken with step 2, that separates "the key
was rejected" from "the row was not found"; either alone could be produced by the other cause.

### 4. Confirm the OpenAPI spec carries the Notifications tag

```powershell
$spec = Invoke-RestMethod "http://localhost:18501/openapi/v1.json"
$operations = foreach ($path in $spec.paths.PSObject.Properties) {
  foreach ($verb in $path.Value.PSObject.Properties) { $verb.Value }
}
"taggedOperations=$(@($operations | Where-Object { $_.tags -ccontains 'Notifications' }).Count)"
"declaredTags=$((@($spec.tags).name | Sort-Object) -join ', ')"
```

**Expected:** `taggedOperations` is non-zero — the notification endpoints are grouped under
`Notifications` rather than falling into whatever group they would otherwise land in. Read from the
operations' own `tags` arrays rather than matched as text, so the assertion cannot be satisfied by the
word appearing somewhere unrelated in the document.

**`declaredTags` names all seven, `Notifications` among them.** It was missing until 2026-08-27 — the
constant existed and the operations carried it, but the spec's top-level `tags` array declared only the
other six, so the group rendered with no description and no ordering. Found by reading the live spec
during #339's PowerShell conversion, and fixed in the same issue.

**The assertion for that now lives at unit tier**, where it belongs:
`OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` fetches the live
`/openapi/v1.json` through the full pipeline and checks every tag an operation carries against the
declared set — derived from the operations themselves, so a tag added to an endpoint and nowhere else
fails without anyone updating a list. This step prints the tags for context rather than re-asserting
what a deterministic test already covers.

### 5. Render the notification pages, and assert on the DOM

**This step needs a browser driver** — the pages are Blazor and the assertions are about what renders,
so an HTTP call cannot make them. They are stated as DOM reads so a driver can run them unattended, and
so two runs check the same thing.

**Expected:** every row of the table below holds, on both pages.

| Page | Assert |
|---|---|
| `/notifications` | `document.title` contains `Notifications`; `tbody tr` count is non-zero; the row text contains the #279 announcement's **body** (`Two REST API operation IDs were renamed`), since the table renders the message rather than the title |
| `/` | body text contains the startup-success wording and the same announcement; `tbody tr` is non-zero |
| both | no stack trace in the body text — `/at [A-Za-z.]+\(/` must not match — and no `503` |

**Capture a screenshot of each as evidence**, but never as the assertion: a screenshot nothing compares
against records what happened without being able to fail. The DOM reads above are what fail.

**Verified this way during #339's full run**, driving a real browser: `/notifications` rendered two
rows with the Created / Type / Message / Expires / Status / Action columns, and `/` rendered
*Quotinator is ready … Startup completed successfully.* with both notifications and the stats block.

### 6. Dismiss every active notification and re-read both pages

```powershell
foreach ($id in (Invoke-RestMethod "http://localhost:18501/api/v1/notifications?pageSize=0").items.id) {
  dotnet script scripts/testing/http.csx -- --method POST `
    --url "http://localhost:18501/api/v1/notifications/$id/dismiss" --expect 200 --status
}

$all = (Invoke-RestMethod "http://localhost:18501/api/v1/notifications?pageSize=0").items
"total=$(@($all).Count) stillActive=$(@($all | Where-Object { -not $_.isDismissed }).Count)"
```

**Expected:** each dismiss returns `200`, and then `stillActive=0` against a non-zero `total`.

**`GET /notifications` returns dismissed rows too** — the API's default is unfiltered, while the
*page's* filter defaults to Active. Do not read a still-populated API response as a failed dismiss;
read the flag, which is why both numbers are printed.

Then, with the driver again:

**Expected:** `/notifications` shows `tbody tr` count `0` and the text *No notifications match this
filter.*; `/` drops its Notifications section entirely rather than leaving an empty heading, and its
stats block still renders.

**Dismissing *every* active row is deliberate.** The empty state is only reachable with none left, and
a fresh container now produces more than one — two when measured. Dismissing just the announcement, as
this step said until #339's full run, leaves the what's-new row behind and the empty state untested.

### 7. Insert the three rows no producer creates

The Status-column, filter and Action-button checks need one `ActionRequired` row carrying a dismiss
trigger, one already-expired row and one already-dismissed row. Nothing in the application produces
these, so the test constructs them — against a **stopped** container, since writing to a SQLite file
underneath a running process is a different scenario:

```powershell
docker stop -t 15 qt-notif-01
docker cp qt-notif-01:/data/quotinatordata.db .claude/temp/notif-01.db

dotnet script scripts/testing/execute-sql.csx -- --db .claude/temp/notif-01.db --sql @'
INSERT INTO System_Notification (Id, Type, Title, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey, DateCreated, IsDeleted) VALUES
  ('a0000278-0000-4000-8000-000000000001','ActionRequired','Smoke test action required','A #278 smoke test row needing an action.',NULL,0,NULL,'DatabaseReset','2026-01-01 00:00:00',0),
  ('a0000278-0000-4000-8000-000000000002','Information','Smoke test expired','A #278 smoke test row that has already expired.','2020-01-01 00:00:00',0,NULL,NULL,'2026-01-01 00:00:00',0),
  ('a0000278-0000-4000-8000-000000000003','Information','Smoke test dismissed','A #278 smoke test row already dismissed.',NULL,1,'2026-01-02 00:00:00',NULL,'2026-01-01 00:00:00',0);
'@

docker cp .claude/temp/notif-01.db qt-notif-01:/data/quotinatordata.db
docker start qt-notif-01
dotnet script scripts/testing/http.csx -- --url "http://localhost:18501/api/v1/health" --wait-for 200 --status

$rows = (Invoke-RestMethod "http://localhost:18501/api/v1/notifications?pageSize=0").items
@($rows | Where-Object { $_.title -like 'Smoke test*' }).Count
```

**Expected:** `execute-sql.csx` reports `3 row(s) affected`, and the count reads `3` — all three rows
are present and readable through the API.

**Counted as objects, not as text matches.** The response is single-line JSON, so a line-counting match
reports `1` however many rows exist — this step required `3` from exactly that shape until #339's full
run, and therefore failed on a correct setup every time. It is the second document in the suite to have
carried this bug; see the index's *A count is evidence only if the instrument counts the right thing*.

**The edit goes through `execute-sql.csx` against a copy of the file**, rather than through a
throwaway container installing `sqlite3` over the network. It is this project's own tool per ADR 010,
and it removes a network dependency from the middle of a test.

**On failure:** a SQL error means the rows were never created, and every assertion in step 8 then
reads an unchanged page. That is a setup failure, not a result — stop.

A CHECK-constraint rejection here is worth reading rather than working around: `Type` and
`DismissTriggerKey` are constrained to their enum's current members, so a failure means the enum moved
and this fixture needs updating with it.

### 8. Check the status column, the status filter, and the Action button

With the three rows from step 7 in place, on `/notifications`. **Driver step**, like step 5 — every
assertion is a DOM read or a click, and each is stated so a driver can perform it without judgement.

**Expected — status column and default filter:** reading `tbody tr`, taking each row's message cell
and status cell —

- The page loads with the **Active** filter selected: exactly the `ActionRequired` row is listed, and
  its Action cell holds a **Run** button.
- Click **All**: all five rows are listed, and their statuses read `Dismissed`, `Dismissed`, `Active`,
  `Expired`, `Dismissed`. **The undismissed row past its `ExpiresAt` reads `Expired`, never `Active`**
  — that is the computed-status assertion.
- Click **Expired only**: exactly one row, the expired one.

**Action button, Cancel path.** Back on **Active**, click **Run** in the ActionRequired row.

- The row's buttons become **Confirm** and **Cancel**.
- Click **Cancel**: the buttons revert to a single **Run**.
- `(Invoke-RestMethod "http://localhost:18501/api/v1/version").database.quotes` is **unchanged** —
  Cancel called nothing.

**Action button, Confirm path.** Click **Run**, then **Confirm**.

- `(Invoke-RestMethod "http://localhost:18501/api/v1/version").database.quotes` drops to `0`, matching
  [`database-lifecycle/03-reset-is-a-full-wipe.md`](../database-lifecycle/03-reset-is-a-full-wipe.md).
- `(Invoke-RestMethod "http://localhost:18501/api/v1/notifications?pageSize=0").items` is **empty** —
  Reset wipes `System_Notification` along with every other table.

**The quote count is what makes Cancel and Confirm distinguishable.** Both leave the page looking
similar; only the domain read separates "reverted without calling anything" from "ran the reset".

**Verified this way during #339's full run**, driving a real browser: the filters behaved exactly as
above, Cancel left `quotes` at `799`, and Confirm dropped it to `0` with `0` notification rows left.

## Observed effect

**Captured 2026-08-25, driving a real browser.** `/notifications` renders a table with Created / Type /
Message / Expires / Status / Action columns and a Show filter offering Active, All and Expired only;
the Message column carries each notification's **body**, not its title. `/` renders
*Quotinator is ready … Startup completed successfully.* above the notification summary and the entity
counts. After every row is dismissed, `/notifications` reads *No notifications match this filter.* and
the modal drops its Notifications section rather than leaving an empty heading.

Driving the ActionRequired row's **Run → Confirm** performed a full database reset from the UI: quotes
`799 → 0`, and the notification table emptied with it.

What the container logs while writing the startup notification has still not been recorded.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-01
Remove-Item .claude/temp/notif-01.db -ErrorAction SilentlyContinue
```

If the Action button's **Confirm** path was exercised, the volume holds a wiped, post-Reset database —
which is why it is removed rather than kept.
