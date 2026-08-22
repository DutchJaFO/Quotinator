# Notifications list, dismiss, render, and drive their action

**Smoke:** yes
**Traces to:** #278

## Preconditions

A fresh container with an admin key, allowed to finish seeding — the notification a fresh container
produces is written during startup.

The Status-filter and Action-button checks need seeded rows that no producer creates on its own. Insert
them directly into `System_Notification` via a SQLite client: one `ActionRequired` row with
`DismissTriggerKey = 'DatabaseReset'`, one already-expired row, and one already-dismissed row.

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

```bash
docker rm -f smoke278
MSYS_NO_PATHCONV=1 docker run -d --name smoke278 -p 8080:8080 \
  -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s -w " [%{http_code}]\n" "http://localhost:8080/api/v1/notifications"
curl -s -w " [%{http_code}]\n" -X POST "http://localhost:8080/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss"
curl -s -w " [%{http_code}]\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/notifications/00000000-0000-0000-0000-000000000000/dismiss"
curl -s "http://localhost:8080/openapi/v1.json" | grep -o '"Notifications"' | head -1
```

**Blazor UI** — visit `http://localhost:8080/notifications` and `http://localhost:8080/`.

**Take an actual screenshot of both.** Page text alone cannot catch a CSS or layout regression, and a
multi-line body is exactly where one shows up.

**Empty state** — dismiss the announcement via `POST /api/v1/notifications/{id}/dismiss`, then reload
both pages.

**Status filter and Action button** — with the seeded rows described in Preconditions in place.

## Expected output

- `GET /notifications` returns `200`, and the response **contains** the announcement titled
  "Two API operation IDs were renamed" — the notification a fresh container is known to produce.
- Dismissing a random id with no `X-Api-Key` returns `401`.
- Dismissing the same id with the correct key returns `404` — no notification exists with that id.
- The OpenAPI spec contains the `Notifications` tag.

**UI** — `/notifications` renders the page heading and #279's announcement row, with no crash and no
503. `/` renders `StartupSuccessModal` with that notification in its summary section.

**Empty state** — after dismissing, `NotificationSummary` renders cleanly with zero rows, rather than an
empty heading with nothing under it.

**Status column** — reads `Active`/`Expired`/`Dismissed` correctly. An undismissed row past its
`ExpiresAt` shows `Expired`, never `Active`.

**Status filter** — defaults to **Active** on page load; **All** shows every row including expired and
dismissed; **Expired only** shows just the expired row.

**Action button** — the `ActionRequired`/`DatabaseReset` row shows a **Run** button. Clicking it
replaces it with **Confirm**/**Cancel**. **Cancel** reverts to the plain **Run** button without calling
the reset endpoint — confirm via the quote count or `/version` staying unchanged. **Confirm** actually
runs `POST /admin/database/reset`: the quote count drops to 0, matching
[`database-lifecycle/03-reset-is-a-full-wipe.md`](../database-lifecycle/03-reset-is-a-full-wipe.md),
and the row disappears afterwards because the whole `System_Notification` table is wiped by Reset like
every other table.

## Observed effect

Partially established. The rendered pages are the observed effect and the screenshots capture them;
what the container logs while writing the startup notification has not been recorded.

## Cleanup

```bash
docker rm -f smoke278
```
