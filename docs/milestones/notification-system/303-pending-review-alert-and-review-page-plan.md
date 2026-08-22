# #303 — Notification + minimal review page: alert when a reseed leaves import actions pending review

**Status:** Planning
**GitHub issue:** #303
**Tiers required:** T1, T2
**Depends on:** #278, #312, #319

---

## Description

When a reseed leaves a file's records genuinely ambiguous, the staging engine (#154) already tracks it
precisely and a full REST review surface already exists — but none of it is reachable from the Blazor
UI or surfaced proactively. An operator only finds out by knowing to check `/import/actions` or by
reading logs.

This issue adds the alert half of that feedback (paired with #302's success half), plus a minimal
in-app way to act on it.

**Scope boundary, confirmed with the developer 2026-08-12:** this is explicitly *not* the full
side-by-side diff/merge editor #66 (Blazor: Import UI milestone) envisions. That stays #66's own,
separately-scoped work.

**This plan needs refining before it can be executed.** The issue's Expected tests table reads
`TBD — decided during this issue's own planning phase`, and step 1 inherits #302's open dedupe-helper
decision. Until both are answered, this is a plan to refine, not a plan to execute.

## Scope revision — where the notification is written from

**Recorded 2026-08-12, relocated here from `overview.md` 2026-08-22.** Same relocation as #302: the
notification write moves into `QuotinatorDatabaseInitializer`'s own per-file seeding loop rather than a
post-hoc read of `LastSeedReport` from `AdminEndpoints.cs`. The existing `else` branch
(`applyResult is not null` — the batch was staged awaiting review, already logged via
`Logger.LogFileStagedAwaitingReview`) is the exact hook point.

The review page in steps 3–5 is unaffected by that relocation.

---

## Steps

### 1. Reuse #302's `INotificationWriter` injection

**Status:** ⬜ Not started

One shared change, not duplicated per issue — whichever of #302/#303 lands first does it, the other
reuses it. That step carries the open decision about where the shared dedupe-write helper lives; read
#302's step 1 before starting here.

### 2. Write the pending-review alert from the staged branch

**Status:** ⬜ Not started

In the `applyResult is not null` branch, one `ActionRequired`-type notification naming the file and a
per-status count (e.g. "File X: 3 pending, 1 blocked"). `applyResult.PendingActionIds` is already
available at that exact point.

### 3. Build the minimal review page

**Status:** ⬜ Not started

A new Blazor page listing every currently active (undecided) `Pending`/`Blocked`/`Stale`
`ImportAction` row across all batches — not scoped to one notification's file. Injects
`IImportActionReader`/`IImportActionService` directly, matching `Notifications.razor`'s own precedent
of using repositories/services rather than round-tripping through REST.

Code-behind partial class per `CLAUDE.md`'s Blazor rules — no inline `@code`, no `@inject`.

### 4. Give each row a basic decide action

**Status:** ⬜ Not started

The field-level keep/replace/custom decision `POST /import/actions/{id}/decide` already accepts. No
side-by-side diff view, no bulk actions, no inline merge editor — all #66's scope.

### 5. Link the notification to the review page

**Status:** ⬜ Not started

### 6. Register the page in navigation and the health gate

**Status:** ⬜ Not started

Add to `NavMenu.razor`, and to `DatabaseHealthGateMiddleware`'s exempt-path list — matching
`/notifications`'s own precedent. The page must stay reachable during a degraded startup, which is
exactly when an operator needs to see what is unresolved.

### 7. Match #302's non-permanent-dedupe behaviour

**Status:** ⬜ Not started

Each reseed run's result is fresh, not deduped against notification history. Check against #312's
opt-in expiry, which removed the always-on aging this assumes.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `INotificationWriter` injection shared with #302, not duplicated | Unit test | TBD — named once #302's step 1 is decided |
| 2 | ❌ | One `ActionRequired` notification per staged file, with per-status counts | Unit test | TBD — named during planning |
| 3 | ❌ | Review page lists every active `Pending`/`Blocked`/`Stale` action across all batches | Live | T1: stage a batch with conflicts, confirm every row appears |
| 4 | ❌ | Each row offers a basic decide action | Live | T1: decide one row, confirm it leaves the active list |
| 5 | ❌ | The notification links to the review page | Live | T1: click through from the notification |
| 6 | ❌ | Page is in `NavMenu` and exempt in `DatabaseHealthGateMiddleware` | Unit test | Exempt-path assertion alongside the existing `/notifications` case |
| 7 | ❌ | Page renders during a degraded startup rather than 500 | Live | T1 + T2: degraded container, page returns 200 |
| 8 | ❌ | No permanent dedupe across reseed runs | Unit test | TBD — named during planning |
