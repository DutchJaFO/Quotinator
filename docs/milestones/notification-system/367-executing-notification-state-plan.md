# #367 — Notification actions give no feedback while they run

**Status:** Waiting for release
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

**Step 1 is settled and this plan is ready to execute** (developer, 2026-09-01): the executing state
lives in a **process-scoped in-memory registry**, not a stored column and not a per-circuit flag. See
the research section below for the three-way comparison that decision was taken on.

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

### 1. Decide where the executing state lives

**Status:** ✅ Done — developer decision, 2026-09-01

A **process-scoped in-memory registry**. Not a stored column (worse than doing nothing on requirement 4
— a process killed mid-run strands a row, and the cleanup code cannot be proven without simulating a
crash) and not a per-circuit flag (invisible across sessions, so it closes none of the double-execution
half). See the research section above for the full comparison.

### 2. Add the registry

**Status:** ✅ Done — rows 1–5 green

`NotificationExecutionState` in `Quotinator.Api.Startup`, registered `AddSingleton` — following
`DatabaseHealthState`'s precedent exactly (a mutable process-wide state object injected into pages,
`Program.cs:623`). `TryBegin(Guid)` admits the first caller and refuses any other until `End(Guid)`;
`IsExecuting(Guid)` answers the display. Lock-guarded, since two circuits can call it at once.

Kept in `Quotinator.Api` because nothing outside it consumes the state. Per CLAUDE.md's relocate-don't-
duplicate rule, this moves to `Quotinator.Data` the moment a second project needs it, and not before.

### 3. Add `Executing` to the display status and render it

**Status:** ✅ Done — rows 6–10 green

`NotificationDisplayStatus` gains `Executing`. `GetDisplayStatus` takes the executing fact as a
parameter rather than reaching for the registry itself, staying a pure static testable without bUnit —
the same shape it has today.

**Precedence is deliberate: Dismissed → Expired → Executing → Active.** A row that finished and
dismissed itself while still in the registry must read `Resolved`, not `Executing`; the window is real,
since dismissal happens inside the action and `End` runs after it returns.

Localised label in all three `UI.*.json` files, same commit.

### 4. Guard the second execution

**Status:** ✅ Done — row 11 green

`ExecuteActionAsync` and `ExecuteChoiceActionAsync` both take the registry before calling the executor
and release it in a `finally`, so a throwing action cannot strand the id within the process either. A
refused call returns without executing.

**Deviation from the plan as written, recorded per process.md.** The plan put the claim/release at each
call site; it went onto `NotificationExecutionState` itself as `RunExclusivelyAsync(id, action)`, and
the page calls that. The reason is the failure mode: a caller who forgets the `finally` strands the
notification as permanently executing for the life of the process — no Run control and no way back —
and there is no way to make "every future caller remembers" testable. Owning the pair inside the type
removes the possibility instead of documenting it.

Consequence for the table: row 11's verification moved from `NotificationsPageTests` (a class that
would have had to exist to reach a private page method) to
`NotificationExecutionStateTests.RunExclusivelyAsync_WhileRunning_DoesNotInvokeTheActionAgain`, and a
row 12 was added for the throwing case, which only became reachable once the guard lived in a testable
place.

The Run control is not offered at all for a row that is executing — refusing after a click is a worse
answer than not presenting the control, and a second session sees the same thing.

**Dismiss is withdrawn too, and that was a T1 finding rather than part of the plan** (developer,
2026-09-01: *"dismiss action is enabled while running… Is there anything to dismiss?"*). The answer is
no, and leaving it live was not merely redundant — it corrupted the recorded outcome. Blazor serialises
circuit events, so a Dismiss clicked during the run queues behind the running handler and is applied
*after* the action has recorded `Resolved`, overwriting it with `Dismissed`. A carried-out action then
reads as one the user declined, which is exactly what #304's reason column exists to prevent.

Reproduced against a container with a negative control before anything was changed:

| Sequence | Reseed completed | Recorded reason |
|---|---|---|
| Run → Confirm, no Dismiss | yes | `resolved` |
| Run → Confirm → Dismiss while the badge reads **Running…** | yes (799 quotes) | `dismissed` |

Both controls are now withdrawn together, so an executing row offers nothing at all — confirmed live:
`buttonsInRow: []` while the badge reads `Running…`, and the run still records `resolved`. The status
badge and its spinner carry the feedback that the controls no longer need to.

### 5. Make the executing state actually visible to the caller

**Status:** ✅ Done — rows 13–16 green

**This is the step that satisfies requirement 1, and it is the one most likely to silently not work.**
Today the handler awaits the executor and only then re-renders, so setting a registry entry changes
nothing the clicker sees. The render has to be flushed *before* the long call — `StateHasChanged`
followed by a yield, so the circuit paints the `Executing` row rather than queuing it behind an
11-second await.

A unit test can prove the registry and the status; only a live run can prove the row actually repaints
mid-action. Row 13 is that proof and is not substitutable by a unit test.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The registry admits the first caller | Unit test | `NotificationExecutionStateTests.TryBegin_FirstCaller_IsAdmitted` |
| 2 | ✅ | It refuses a second caller while the first holds | Unit test | `NotificationExecutionStateTests.TryBegin_WhileHeld_IsRefused` |
| 3 | ✅ | It admits again once the first releases | Unit test | `NotificationExecutionStateTests.TryBegin_AfterEnd_IsAdmittedAgain` |
| 4 | ✅ | Two different notifications do not block each other | Unit test | `NotificationExecutionStateTests.TryBegin_DifferentIds_BothAdmitted` |
| 5 | ✅ | Concurrent callers produce exactly one winner | Unit test | `NotificationExecutionStateTests.TryBegin_ConcurrentCallers_AdmitsExactlyOne` |
| 6 | ✅ | An executing active row reads `Executing` | Unit test | `NotificationTableTests.GetDisplayStatus_Executing_ReportsExecuting` |
| 7 | ✅ | A dismissed row reads its dismiss reason even while still in the registry | Unit test | `NotificationTableTests.GetDisplayStatus_DismissedWhileExecuting_ReportsTheDismissReason` |
| 8 | ✅ | An expired row still reads `Expired` | Unit test | `NotificationTableTests.GetDisplayStatus_ExpiredWhileExecuting_ReportsExpired` |
| 9 | ✅ | `Executing` has a non-empty label in all three languages | Unit test | `TranslationCompletenessTests` (existing) + `NotificationTableTests.EveryDisplayStatus_HasATranslationKey` |
| 10 | ✅ | The Run control is not offered for an executing row | Unit test | `NotificationTableTests.ShowsRunControl_WhileExecuting_IsFalse` |
| 11 | ✅ | A second execution of the same notification does not reach the executor | Unit test | `NotificationExecutionStateTests.RunExclusivelyAsync_WhileRunning_DoesNotInvokeTheActionAgain` — moved from `NotificationsPageTests`, see step 4's deviation |
| 12 | ✅ | A throwing action still releases its claim | Unit test | `NotificationExecutionStateTests.RunExclusivelyAsync_ActionThrows_StillReleasesTheClaim` — added by the same deviation |
| 13 | ✅ | The row visibly reads `Executing` *during* a real run, not only after | Live (T2) + screenshot | [12-running-action-state.md](../../automated-testing/notifications-and-changelog/12-running-action-state.md) step 2 — badge reads **Running…**, Run control gone |
| 14 | ✅ | A second click during a run produces exactly one reseed | Live (T2) | same document, step 3 — `reseed requested` appears exactly once |
| 15 | ✅ | The row reads `Done`, not `Executing`, once the action completes | Live (T2) | same document, step 3 — `dismissed=True reason=resolved`, rendered **Done** |
| 16 | ✅ | A restart during a run leaves no row reading `Executing` | Live (T2) | same document, step 4 — restarted mid-run (13 quotes, interrupted), page carries no `Running` |
| 17 | ✅ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 18 | ✅ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 19 | ✅ | The running badge carries a spinner that is actually animating | Live (T2) + screenshot | [12-running-action-state.md](../../automated-testing/notifications-and-changelog/12-running-action-state.md) step 2 — `animationName: spinner-border`, `iteration: infinite`, `playState: running`, and none present once the run settles |
| 20 | ✅ | Dismissing during a run cannot mislabel the outcome | Unit test + Live (T2) | `NotificationTableTests.ShowsDismissControl_WhileExecuting_IsFalse`; same document, step 2 — the row offers no controls at all while running, and the action still records `resolved` |

**Row 13 is the one that cannot be replaced by a unit test.** Every row above it can pass against an
implementation whose UI never repaints until the action finishes — which is the exact defect this issue
was raised for. Absence of a repaint proves nothing; row 13 asserts a positive.

**The test list is completed before implementation starts, not during it** — #302's own retrospective
found its table growing from 22 rows to 29 mid-flight, which is what the Definition of done's
"start red before implementation" box exists to prevent. See
[#302's plan](302-clean-reseed-confirmation-notification-plan.md)'s deviation section.
