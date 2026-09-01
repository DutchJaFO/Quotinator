# #367 — Notification actions give no feedback while they run

**Status:** Planning
**GitHub issue:** #367
**Tiers required:** T1, T2
**Depends on:** #278, #304
**Order:** moved up to 16, ahead of #308 (developer, 2026-09-01)

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

## Research, 2026-09-01 — two facts that reshape step 1

**The Status column is already derived, not stored.** `NotificationTable.GetDisplayStatus(notification,
now)` computes `NotificationDisplayStatus { Active, Expired, Dismissed, Resolved, Obsolete }` at render
time from `IsDismissed`, `DismissReason` and `ExpiresAt`. There is no status column to extend, so
adding an `Executing` member is a display-layer change by default — a schema change is only needed if
the *fact* of executing has to be persisted, which is a separate question from where the label lives.

**`SharedSeedLock` is process-wide, not per-request.** `DatabaseInitializer.SeedLock` is a
`static SemaphoreSlim(1, 1)`, so a second reseed does not run concurrently — it queues and then performs
a second full reseed once the first releases. The issue's consequence (2) is accurate, and it is a
*sequencing* fault rather than a concurrency one.

Together these separate the two requirements the issue bundles:

- **Requirement 1 (tell the user it started)** is satisfied by anything the clicking circuit can see.
- **Requirement 2 (prevent a second execution)** needs a fact shared across circuits and sessions — but
  not necessarily a durable one.

That opens a third option the issue's binary framing does not name: a **process-scoped in-memory
registry** of notification ids currently executing, registered as a singleton. `GetDisplayStatus` takes
it as a parameter (staying a pure static, testable without bUnit, exactly as it is now), the executor
consults it before starting, and a second session sees the row as `Executing` with no Run control
rather than being refused after clicking.

Its properties against the issue's own four requirements:

| | Per-circuit flag | Process registry | Stored column |
|---|---|---|---|
| 1. Shows it started | ✅ | ✅ | ✅ |
| 2. Blocks a second run | ❌ invisible across sessions | ✅ | ✅ |
| 4. Cannot strand after a restart | ✅ free | ✅ free | ❌ needs explicit startup cleanup |
| Cost | none | a singleton | column + migration + CHECK (ADR 008) |

The stored column is the only option that is *worse* on requirement 4 than doing nothing: a process
killed mid-reseed leaves a row marked executing with nothing running, and clearing it needs startup
code whose correctness is itself untestable without simulating a crash. The registry gets requirement 4
by construction — the fact dies with the process that owned it, which is exactly right, because the
execution died with it too.

**Boundary condition, stated rather than assumed:** a process registry is correct only while Quotinator
is one process. It is — single container by design, and the HA supervisor runs single-container add-ons
(see CLAUDE.md's "Why Quotinator.Api hosts the Blazor UI"). If that ever stops being true, this becomes
a distributed-lock problem that a stored column would not have solved either.

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
