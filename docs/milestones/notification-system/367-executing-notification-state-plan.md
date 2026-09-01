# #367 — Notification actions give no feedback while they run

**Status:** Planning
**GitHub issue:** #367
**Tiers required:** T1, T2
**Depends on:** #278, #304

---

## Description

Running an `Action-required` notification's remedy from `/notifications` shows nothing between
confirming and completion. Found in #302's T1 pass (2026-09-01): a reseed took ~11 seconds during which
the row still read `Active` with an enabled **Run** button, and the first visible change was the
finished result.

**This plan needs refining before it can be executed.** Step 1 is an open design decision — whether the
executing state is stored or transient — and it decides what the remaining steps and every test look
like. Until it is answered, this is a plan to refine, not a plan to execute.

## Background from #302's T1

The confirmation step before execution works correctly and is not in question; the gap is strictly the
window after confirming.

`Notifications.razor.cs`'s `ExecuteActionAsync` awaits `INotificationActionExecutor.ExecuteAsync` and
only then calls `LoadAsync`, so nothing is signalled to the UI in between. It also calls the executor
**in-process**, which means the `admin` concurrency-1 rate-limit policy never applies — a second
confirmed click during the run queues on `SharedSeedLock` and performs a second full reseed.

---

## Steps

### 1. Decide whether the executing state is stored or transient

**Status:** ⬜ Not started — **blocks every step below**

A transient per-circuit flag is far cheaper, but it is lost on a page refresh, invisible to any other
session, and therefore does not actually prevent a second execution. A stored marker survives both, at
the cost of a column and a migration.

The answer decides whether steps 2–4 involve a schema change, and it decides every test name below, so
it is settled before the verification table is filled in — not during implementation.

### 2. Add the executing state and render it

**Status:** ⬜ Not started

A state distinct from `Active`/`Dismissed`/`Resolved`/`Expired`, rendered in `NotificationTable`'s
Status column with a localised label in all three `UI.*.json` files.

### 3. Prevent a second execution while one is in flight

**Status:** ⬜ Not started

Whether this is enforceable at all depends on step 1 — a transient flag cannot enforce it across a
refresh or a second session.

### 4. Decide what happens when execution fails or the process restarts mid-run

**Status:** ⬜ Not started

A notification must not be strandable as permanently "executing". A stored state needs an explicit
answer here; a transient one gets it for free by disappearing, which is also why it cannot satisfy
step 3.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | TBD — every row below is named once step 1 is decided | | |

**The test list is completed before implementation starts, not during it** — #302's own retrospective
found its table growing from 22 rows to 29 mid-flight, which is what the Definition of done's
"start red before implementation" box exists to prevent. See
[#302's plan](302-clean-reseed-confirmation-notification-plan.md)'s deviation section.
