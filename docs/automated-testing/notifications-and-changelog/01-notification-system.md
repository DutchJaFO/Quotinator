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
- The empty-state check requires dismissing the announcement **first** — a fresh container is no longer
  empty, so the genuinely-zero-rows path is otherwise never exercised.

## Steps

### 1. Start a container of this test's own and list its notifications

```bash
docker rm -f qt-notif-01 2>/dev/null; docker volume rm qt-notif-01-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-notif-01 -p 18501:8080 -v qt-notif-01-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:18501/api/v1/health > /dev/null; do sleep 1; done
curl -s -w " [%{http_code}]\n" "http://localhost:18501/api/v1/notifications"
```

**Expected:** `GET /notifications` returns `200`, and the response **contains** the announcement titled
"Two API operation IDs were renamed" — the notification a fresh container is known to produce.

**On failure:** an empty list means seeding had not finished writing the startup notification, not that
no notification is produced — the two are indistinguishable from an early read (see Preconditions).
Stop and let the container finish rather than reading the absence as a result.

### 2. Dismiss an unknown notification with no admin key

```bash
curl -s -w " [%{http_code}]\n" -X POST "http://localhost:18501/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss"
```

**Expected:** `401`.

### 3. Dismiss the same all-zero id with the correct key

```bash
curl -s -w " [%{http_code}]\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18501/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss"
```

**Expected:** `404` — no notification exists with that id.

### 4. Confirm the OpenAPI spec carries the Notifications tag

```bash
curl -s "http://localhost:18501/openapi/v1.json" | grep -o '"Notifications"' | head -1
```

**Expected:** the OpenAPI spec contains the `Notifications` tag.

### 5. Render the notification pages in a browser

**Blazor UI** — visit `http://localhost:18501/notifications` and `http://localhost:18501/`.

**Take an actual screenshot of both.** Page text alone cannot catch a CSS or layout regression, and a
multi-line body is exactly where one shows up.

**Expected:** `/notifications` renders the page heading and #279's announcement row, with no crash and
no 503. `/` renders `StartupSuccessModal` with that notification in its summary section.

### 6. Dismiss the announcement and reload both pages

**Empty state** — dismiss the announcement via `POST /api/v1/notifications/{id}/dismiss`, then reload
both pages.

**Expected:** after dismissing, `NotificationSummary` renders cleanly with zero rows, rather than an
empty heading with nothing under it.

### 7. Insert the three rows no producer creates

The Status-column, filter and Action-button checks need one `ActionRequired` row carrying a dismiss
trigger, one already-expired row and one already-dismissed row. Nothing in the application produces
these, so the test constructs them — against a **stopped** container, since writing to a SQLite file
underneath a running process is a different scenario:

```bash
docker stop -t 15 qt-notif-01
MSYS_NO_PATHCONV=1 docker run --rm -v qt-notif-01-data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 /data/quotinatordata.db \
   \"INSERT INTO System_Notification (Id, Type, Title, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey, DateCreated, IsDeleted) VALUES
     ('a0000278-0000-4000-8000-000000000001','ActionRequired','Smoke test action required','A #278 smoke test row needing an action.',NULL,0,NULL,'DatabaseReset','2026-01-01 00:00:00',0),
     ('a0000278-0000-4000-8000-000000000002','Information','Smoke test expired','A #278 smoke test row that has already expired.','2020-01-01 00:00:00',0,NULL,NULL,'2026-01-01 00:00:00',0),
     ('a0000278-0000-4000-8000-000000000003','Information','Smoke test dismissed','A #278 smoke test row already dismissed.',NULL,1,'2026-01-02 00:00:00',NULL,'2026-01-01 00:00:00',0);\""
docker start qt-notif-01
until curl -sf http://localhost:18501/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:18501/api/v1/notifications?pageSize=0" | grep -c "Smoke test"
```

**Expected:** `sqlite3` completes with no error, and the count is `3` — all three rows are present and
readable through the API.

**On failure:** a `sqlite3` error means the rows were never created, and every assertion in step 8 then
reads an unchanged page. That is a setup failure, not a result — stop.

A CHECK-constraint rejection here is worth reading rather than working around: `Type` and
`DismissTriggerKey` are constrained to their enum's current members, so a failure means the enum moved
and this fixture needs updating with it.

### 8. Check the status column, the status filter, and the Action button

With the three rows from step 7 in place.

**Expected — status column:** reads `Active`/`Expired`/`Dismissed` correctly. An undismissed row past
its `ExpiresAt` shows `Expired`, never `Active`.

**Expected — status filter:** defaults to **Active** on page load; **All** shows every row including
expired and dismissed; **Expired only** shows just the expired row.

**Expected — Action button:** the `ActionRequired`/`DatabaseReset` row shows a **Run** button. Clicking
it replaces it with **Confirm**/**Cancel**. **Cancel** reverts to the plain **Run** button without
calling the reset endpoint — confirm via the quote count or `/version` staying unchanged. **Confirm**
actually runs `POST /admin/database/reset`: the quote count drops to 0, matching
[`database-lifecycle/03-reset-is-a-full-wipe.md`](../database-lifecycle/03-reset-is-a-full-wipe.md),
and the row disappears afterwards because the whole `System_Notification` table is wiped by Reset like
every other table.

## Observed effect

Partially established. The rendered pages are the observed effect and the screenshots capture them;
what the container logs while writing the startup notification has not been recorded.

## Cleanup

```bash
docker rm -f qt-notif-01 2>/dev/null
docker volume rm qt-notif-01-data
```

If the Action button's **Confirm** path was exercised, the volume holds a wiped, post-Reset database —
which is why it is removed rather than kept.
