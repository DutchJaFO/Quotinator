# #293 — NotificationSummary/notifications crash when System_Notification doesn't exist yet

**Status:** In progress
**GitHub issue:** #293
**Tiers required:** T1, T2
**Depends on:** none

---

## Background

Found live during a real HA v1.8.2 → v1.8.3-beta upgrade attempt (2026-08-10 22:28 UTC) that failed
partway through the squashed Data migration (#289) with `SQLite Error 14: 'unable to open database
file'` — root cause not yet confirmed, tracked separately as a retry-and-observe step outside this
issue's scope. The pre-migration backup/restore mechanism worked correctly, leaving the database intact
at its pre-migration state (schema v2/v4). But every subsequent page load crashed instead of showing
#263's intended degraded-state UI, because `NotificationSummary` (embedded in Home's modal) and the
`/notifications` page both query `System_Notification`, which genuinely doesn't exist yet on that
restored database.

**Verified before starting:** confirmed live via `git stash`/manual revert that the two new tests
(below) are genuinely red against the pre-fix `NotificationReader`, reproducing the exact live error
message (`no such table: System_Notification`) — not assumed.

## Approach

Fix at the `NotificationReader` level, not the Blazor component level — both `GetActiveNotificationsAsync`
(Home's modal) and `GetPagedAsync` (the `/notifications` page, confirmed reachable during degraded state
via `DatabaseHealthGateMiddleware`'s exempt list) hit the same underlying gap, so a single fix at the
data layer covers both callers. Catch `SqliteException` narrowly scoped to `SqliteErrorCode == 1` AND
the message containing `"no such table: System_Notification"` specifically — not a blanket catch-all,
to avoid masking a genuinely different error. This is deliberately not the same class of thing as
CLAUDE.md's "No exception-based migration recovery" policy: that policy governs `InitialiseAsync`'s own
version-vs-schema mismatch handling (a real structural problem that must hard-fail), whereas this is a
read-only, display-only query reached from pages that are *already* known-degraded and *designed* to
stay reachable in that state — "no active notifications" is the only correct response, not a 500.

## Steps

### 1. Fix `NotificationReader`

**Status:** Done.

Both `GetActiveNotificationsAsync` and `GetPagedAsync` wrap their query in a `try`/`catch
(SqliteException ex) when (IsMissingNotificationTable(ex))`, returning an empty result. Shared private
helper `IsMissingNotificationTable` checks both `SqliteErrorCode == 1` and the message text, so an
unrelated SQLite error (a genuine `SQLITE_ERROR` for a different reason) still propagates normally.

### 2. Add failing-then-passing tests

**Status:** Done.

`GetActiveNotificationsAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing` and
`GetPagedAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing` — each builds a real SQLite file with no
`System_Notification` table, confirmed red against the pre-fix reader (exact live error message
reproduced), green after.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `GetActiveNotificationsAsync` returns empty instead of throwing when the table doesn't exist | Unit test | `GetActiveNotificationsAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing` |
| 2 | ✅ | `GetPagedAsync` returns empty instead of throwing when the table doesn't exist | Unit test | `GetPagedAsync_TableDoesNotExist_ReturnsEmptyInsteadOfThrowing` |
| 3 | ✅ | Both tests confirmed genuinely red against the pre-fix code | Live (review) | Manual revert of `NotificationReader.cs` only (tests kept), reran — both failed with the exact live error message; fix restored afterward |
| 4 | ✅ | No regression | Unit test | `dotnet test tests/Quotinator.Data.Tests` — 1076 tests, 0 failures; full solution build clean |
| 5 | ⬜ | T1 — app starts cleanly with the fix in place | Live (T1) | Pending |
| 6 | ✅ | T2 — Docker smoke test reproducing the exact live scenario (real v1.8.2 db, migration failure, degraded-state page load) | Live (Docker) | `docs/smoke-tests.md` Section 38, live-run 2026-08-11 — confirmed `System_Notification` genuinely absent on a real v1.8.2 db; forced migration failure reproduced `schemaVersion: 0`/`503 unhealthy`; `/`, `/stats`, `/notifications` all rendered `200` with correct content (`StartupErrorModal`, zero-count stats, `No notifications yet.`) instead of crashing; `GET /api/v1/notifications` correctly `503`-gated |

---

## Notes

The original migration failure's root cause (`SQLite Error 14`) is explicitly out of scope for this
issue — this issue only fixes the downstream crash that made the degraded state unusable. A retry of
the same live upgrade is the agreed next diagnostic step for the root cause, tracked separately.
