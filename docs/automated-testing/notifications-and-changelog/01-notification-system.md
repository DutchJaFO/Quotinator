# Notifications list, dismiss, render, and drive their action

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #278

## Preconditions

**Beyond the profile.** One container of this test's own (`smoke278` / `smoke278-data`) rather than
`qt-env`, because the Action-button check runs a Reset that wipes the database it is started against.
Seeding must be allowed to finish before anything is asserted — the notification a fresh container
produces is written during startup, so an early read cannot tell "not produced" from "not yet".

The Status-filter and Action-button checks additionally need three rows that no producer creates on its
own, inserted directly into `System_Notification`: one `ActionRequired` row with
`DismissTriggerKey = 'DatabaseReset'`, one already-expired row, and one already-dismissed row.

**No command — the three rows are described but never created.** Writing the `INSERT` here would mean
inventing the column set and the values for `Type`, `ExpiresAt` and `IsDismissed` that the description
does not give, so it is flagged rather than guessed. Until it is written, the Status-filter and
Action-button assertions below have no setup and cannot be run.

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
docker rm -f smoke278 2>/dev/null; docker volume rm smoke278-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name smoke278 -p 8080:8080 -v smoke278-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s -w " [%{http_code}]\n" "http://localhost:8080/api/v1/notifications"
```

**Expected:** `GET /notifications` returns `200`, and the response **contains** the announcement titled
"Two API operation IDs were renamed" — the notification a fresh container is known to produce.

**On failure:** an empty list means seeding had not finished writing the startup notification, not that
no notification is produced — the two are indistinguishable from an early read (see Preconditions).
Stop and let the container finish rather than reading the absence as a result.

### 2. Dismiss an unknown notification with no admin key

```bash
curl -s -w " [%{http_code}]\n" -X POST "http://localhost:8080/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss"
```

**Expected:** `401`.

### 3. Dismiss the same all-zero id with the correct key

```bash
curl -s -w " [%{http_code}]\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss"
```

**Expected:** `404` — no notification exists with that id.

### 4. Confirm the OpenAPI spec carries the Notifications tag

```bash
curl -s "http://localhost:8080/openapi/v1.json" | grep -o '"Notifications"' | head -1
```

**Expected:** the OpenAPI spec contains the `Notifications` tag.

### 5. Render the notification pages in a browser

**Blazor UI** — visit `http://localhost:8080/notifications` and `http://localhost:8080/`.

**Take an actual screenshot of both.** Page text alone cannot catch a CSS or layout regression, and a
multi-line body is exactly where one shows up.

**Expected:** `/notifications` renders the page heading and #279's announcement row, with no crash and
no 503. `/` renders `StartupSuccessModal` with that notification in its summary section.

### 6. Dismiss the announcement and reload both pages

**Empty state** — dismiss the announcement via `POST /api/v1/notifications/{id}/dismiss`, then reload
both pages.

**Expected:** after dismissing, `NotificationSummary` renders cleanly with zero rows, rather than an
empty heading with nothing under it.

### 7. Check the status column, the status filter, and the Action button

**Status filter and Action button** — with the three rows described in Preconditions in place. That
insert has no command yet; see the flag there.

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
docker rm -f smoke278 2>/dev/null
docker volume rm smoke278-data
```

The container and volume are this test's own, so restoring the profile clears nothing it made. If the
Action button's **Confirm** path was exercised, the volume holds a wiped, post-Reset database — which
is why it is removed rather than kept.
