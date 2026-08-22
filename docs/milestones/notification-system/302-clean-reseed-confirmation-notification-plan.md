# #302 — Notification: confirm files that reseed cleanly with no review needed

**Status:** Planning
**GitHub issue:** [#302](https://github.com/DutchJaFO/Quotinator/issues/302)
**Tiers required:** T1, T2
**Depends on:** #278, #312, #319

---

## Description

A reseed already reports a per-file breakdown, but only in the API response and the log. This issue
adds the "everything's fine" half of that feedback to the UI: one `Success` notification per file that
reseeded with nothing left to review.

**This plan needs refining before it can be executed.** Step 1 is an open design decision the issue
itself defers here (the shared dedupe-write helper), and the issue's Expected tests table reads
`TBD — decided during this issue's own planning phase`. Until step 1 is answered and the verification
table's test names are real, this is a plan to refine, not a plan to execute.

## Scope revision — where the notification is written from

**Recorded 2026-08-12, relocated here from `overview.md` 2026-08-22.** The notification write moves
into the seeding pipeline itself rather than being a separate call bolted onto `AdminEndpoints.cs`
after `ReseedAsync()` returns. Per developer direction: new notification content comes from the same
mechanism that already handles import content, not one-off `INotificationWriter.WriteAsync` calls
scattered across unrelated call sites.

`QuotinatorDatabaseInitializer`'s own per-file seeding loop already carries the exact signal at the
exact moment — `_actionService.ApplyBatchAsync(batchIdStr, ...)` returning `null` means the batch
fully applied with zero pending actions. The notification is written from inside that branch, not
reconstructed afterward from a snapshot report.

This surfaced the dependency-direction gap step 1 owns: the existing dedupe helper lives in
`Quotinator.Api`, unreachable from `Quotinator.Core` where the seeding loop is.

---

## Steps

### 1. Decide where the shared dedupe-write helper lives

**Status:** ⬜ Not started — **blocks every step below**

`NotificationSeeding.SeedOnceAsync` sits in `Quotinator.Api.Startup`, unreachable from
`Quotinator.Core` (dependency direction is `Api` → `Core`, never the reverse). This issue may not need
permanent dedupe at all — see step 4 — but #303 and #304 do, and #304's own content-change trigger has
the identical reachability problem.

Decide: relocate a shared version into `Quotinator.Data` now so all three issues use one piece, or
leave the shipped Api-only call sites (#279, #289) untouched and share only the relocated version
going forward. #312 was expected to absorb this decision — confirm whether it did before re-deciding it
here.

### 2. Inject `INotificationWriter` into `QuotinatorDatabaseInitializer`

**Status:** ⬜ Not started

Matches its existing pattern of taking `Quotinator.Data.Repositories` dependencies
(`IImportBatchRepository`, `IImportActionReader`/`Writer`). No project-boundary change —
`Quotinator.Core` already depends on `Quotinator.Data`.

Shared with #303: whichever lands first does this step, the other reuses it.

### 3. Write the per-file success notification from the clean-apply branch

**Status:** ⬜ Not started

In the `applyResult is null` branch, one `Success`-type notification naming the file and summarising
what was added or modified — **only** when the run is an explicit reseed (`OnReseedAsync`), never the
first empty-database seed at cold start. `DatabaseStatsSummary` already covers cold start with
aggregate counts, and per-file notifications on a brand-new install would be pure clutter.

### 4. Rely on configured expiry rather than permanent dedupe

**Status:** ⬜ Not started

A reseed is a repeatable action and each run's per-file result is fresh information worth its own
notification. No "write once forever" dedupe; the existing
`Quotinator:NotificationDefaultExpiryHours` ages them out.

Check this against #312's opt-in expiry before implementing — #312 removed always-on expiry, which is
the mechanism this step currently assumes.

### 5. Write nothing for a reseed that touches zero files

**Status:** ⬜ Not started

E.g. no configured sources. No notification at all, not an empty one.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `INotificationWriter` injected into `QuotinatorDatabaseInitializer` | Unit test | TBD — named once step 1 is decided |
| 2 | ❌ | One `Success` notification per cleanly-applied file, on reseed only | Unit test | TBD — named once step 1 is decided |
| 3 | ❌ | No per-file notification on the first empty-database seed | Unit test | TBD — named once step 1 is decided |
| 4 | ❌ | Shared dedupe-write helper is reachable from `Quotinator.Core` | Unit test | TBD — named once step 1 is decided |
| 5 | ❌ | No permanent dedupe; notifications age out via configured expiry | Unit test | TBD — named once step 1 is decided |
| 6 | ❌ | No notification for a reseed that touches zero files | Unit test | TBD — named once step 1 is decided |
| 7 | ❌ | Notifications render correctly after a live reseed | Live | T1: trigger a reseed, confirm one notification per cleanly-applied file in the startup modal and `/notifications` |
