# #177 — ImportBatches.Status never set to Applied via the staged decide→apply flow, breaking reversal

**Status:** Planning
**GitHub issue:** #177
**Tiers required:** T1, T2
**Depends on:** none technically; sequenced first under #217 because the resolve→apply→reverse→retry
cycle #181/#153's own testing methodology relies on needs a working `POST /import/actions/reverse`

---

## Spec requirements (from the GitHub issue)

1. Once every action in a batch has successfully applied via the two-phase review→decide→apply flow,
   the owning `ImportBatches` row's `Status` is set to `Applied` (and `AppliedAt` populated) — the same
   way the single-shot direct-apply path already does — making the batch genuinely reversible via
   `POST /api/v1/import/actions/reverse`.
2. `tests/Quotinator.Core.Tests/Services/SqliteImportActionServiceTests.cs`'s private
   `MarkImportBatchAppliedAsync` test-only helper (raw SQL `UPDATE ImportBatches SET Status =
   'Applied'`), currently called manually by every reverse-related test after `ApplyBatchAsync`, should
   no longer be needed once the real production path sets this itself — remove it and let those tests
   exercise the real path instead of working around the gap.

**Note on the issue's own text**: it still names `Quotinator.Engine.Tests.Services.
SqliteImportActionServiceTests` throughout (both the "Failing tests" table and the
`MarkImportBatchAppliedAsync` reference) — stale since #206 merged `Quotinator.Engine` into
`Quotinator.Core`. The real location today is `tests/Quotinator.Core.Tests/Services/
SqliteImportActionServiceTests.cs`, confirmed present with the described helper still in place
(lines 480, 493, 514, 580, 635). The issue body should be corrected to the current path before/while
implementing.

**Expected tests** (from the issue's own table, both starting red):

| Test class | Test method |
|---|---|
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `ApplyBatchAsync_TwoPhaseFlow_MarksImportBatchStatusApplied` |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `ReverseBatchAsync_TwoPhaseFlowBatch_SucceedsWithoutManualStatusOverride` |

---

## Investigation findings (re-verified 2026-07-25, before starting implementation)

**The bug is still live and exactly as described.** `ImportActionResolutionCoordinator.
TryApplyBatchAsync` (`src/Quotinator.Data/Import/ImportActionResolutionCoordinator.cs:77-111`) marks
every individual `SystemImportAction` `Applied` via `_writer.MarkAppliedAsync` (line 106) but never
touches `ImportBatches.Status` at all. `SqliteImportActionService.ApplyBatchAsync`
(`src/Quotinator.Core/Services/SqliteImportActionService.cs:379-389`) — the method
`POST /import/actions/apply` calls directly (`ImportEndpoints.cs:339`) — is a thin wrapper around
`TryApplyBatchAsync` and adds nothing on top. `ReverseBatchAsync`
(`SqliteImportActionService.cs:509`) checks `batch.Status.Parsed != ImportBatchStatus.Applied` and
throws `ImportBatchStateException` when it doesn't match — confirmed this is the exact throw path a
two-phase-applied batch hits.

**The asymmetry the issue describes is real, and its root cause is now more precisely located than the
issue text states.** `SqliteQuoteImportService.cs` has *two* call sites (`ApplyStagedBatchAsync` at
line ~161, and the inline non-preview branch of the main import method at line ~108) that each: call
`_actionService.ApplyBatchAsync(...)` (the *same* `SqliteImportActionService.ApplyBatchAsync` the
`/actions/apply` route calls directly), check whether the result is `null` (meaning "fully applied,
nothing left pending"), and only then set `batch.Status = Applied`, `batch.AppliedAt`, and
`batch.RecordCount` themselves before calling `_importBatches.UpdateAsync(batch)`. In other words: the
status-setting logic already exists, twice, duplicated in `SqliteQuoteImportService.cs` — but only for
callers that route through it (`POST /import?batchId=` and `POST /import/preview`'s non-preview
sibling). `POST /import/actions/apply`, which calls `SqliteImportActionService.ApplyBatchAsync`
directly with no such wrapper, gets none of it.

**Recommended fix location — a genuine design choice, not silently picked.** Two candidates:

1. **Duplicate the fix a third time**, adding the same `Status = Applied`/`AppliedAt` logic directly
   into `ImportEndpoints.cs`'s `/actions/apply` handler (or into `SqliteImportActionService.
   ApplyBatchAsync` itself). Fast, but perpetuates the exact duplication pattern that let this bug ship
   in the first place — a fourth caller of `ApplyBatchAsync` in the future would need to remember the
   same fix a fourth time.
2. **Move `Status = Applied`/`AppliedAt` into `SqliteImportActionService.ApplyBatchAsync` itself** — the
   one shared choke point every caller (the direct `/actions/apply` route, and both of
   `SqliteQuoteImportService.cs`'s call sites) already goes through. `SqliteQuoteImportService.cs`'s two
   call sites then only need to keep their own `RecordCount` computation (`imported + updated`, which is
   Quote-import-specific counting logic that does not generalize to a Source/StageDirection/SoundCue
   batch and so must stay where it is), dropping their now-redundant `Status`/`AppliedAt` lines and
   still calling `_importBatches.UpdateAsync(batch)` — or, better, `IImportBatchRepository.
   UpdateRecordCountAsync` (already exists, `IImportBatchRepository.cs:22`) directly, avoiding a
   `GetByIdAsync`-then-`UpdateAsync` round trip these two call sites already do purely to set
   `RecordCount`.

**Recommendation: approach 2** — it fixes the reported bug and removes the pre-existing duplication in
the same change, matching this project's established choke-point pattern (`IdClauses`,
`GuidExtensions.ToCanonicalId`). `SqliteImportActionService` already has `_importBatchRepository`
injected (`SqliteImportActionService.cs:33`), so no new dependency is needed. Confirm this approach
before implementing — a different design isn't wrong, but this is the one this plan doc assumes below.

**This is entity-agnostic, confirming the issue's own framing.** `TryApplyBatchAsync`/`ApplyBatchAsync`
have no entity-specific branching at the batch-status level — a Source, StageDirection, or SoundCue
batch applied via the two-phase flow hits the identical gap as a Quote batch. The fix at the shared
choke point (approach 2) fixes all of them at once; a per-entity-type test is not needed, one
representative entity type (Quote, matching the issue's own reproduction steps) is sufficient.

---

## Steps

### 1. Write the red tests

**Status:** Not started.

`ApplyBatchAsync_TwoPhaseFlow_MarksImportBatchStatusApplied`: stage a batch under `review` policy with
one changed field, decide it, call `ApplyBatchAsync`, then assert (via `_importBatchRepository.
GetByIdAsync` or equivalent) that `ImportBatches.Status == Applied` and `AppliedAt` is populated —
without calling the existing `MarkImportBatchAppliedAsync` test helper first. Confirm red (helper
removed or bypassed) before writing the fix.

`ReverseBatchAsync_TwoPhaseFlowBatch_SucceedsWithoutManualStatusOverride`: same staging/decide/apply
sequence, then call `ReverseBatchAsync` directly — must succeed (no `ImportBatchStateException`)
without any manual `Status` override in the test itself. Confirm red first.

### 2. Remove the test-only workaround

**Status:** Not started.

Delete `SqliteImportActionServiceTests.MarkImportBatchAppliedAsync` and every call site (lines 480,
493, 514, 580, 635 as of this plan doc) once the real fix makes it unnecessary — these existing tests
should continue passing through the real production path instead of the raw-SQL workaround. This is
itself a form of red/green validation: if any of these tests still needs the helper after the fix
lands, the fix is incomplete.

### 3. Implement the fix

**Status:** Not started.

Per the recommended approach above: move `Status = Applied`/`AppliedAt` population into
`SqliteImportActionService.ApplyBatchAsync`, gated on `TryApplyBatchAsync` returning `null` (the
existing "nothing left pending" signal both `SqliteQuoteImportService.cs` call sites already check).
Remove the now-duplicate `batch.Status`/`batch.AppliedAt` lines from `SqliteQuoteImportService.cs`'s
two call sites, keeping only their `RecordCount` update (via `_importBatches.UpdateRecordCountAsync`
directly, replacing the `GetByIdAsync`-then-mutate-then-`UpdateAsync` round trip if that simplification
doesn't change observable behaviour — confirm via the existing `SqliteQuoteImportService` test suite
staying green).

### 4. Confirm no regression in related tests

**Status:** Not started.

Full suite green, particular attention to every existing `ReverseBatchAsync`/`ApplyBatchAsync` test in
`SqliteImportActionServiceTests.cs` and `SqliteQuoteImportServiceTests.cs` (or wherever
`SqliteQuoteImportService`'s own tests live) — the `RecordCount` simplification in step 3 must not
change any asserted value.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A two-phase decide→apply batch sets `ImportBatches.Status = Applied` and populates `AppliedAt` | Unit test | `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests.ApplyBatchAsync_TwoPhaseFlow_MarksImportBatchStatusApplied` — starts red |
| 2 | ❌ | A two-phase-applied batch can be reversed without a manual status override | Unit test | `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests.ReverseBatchAsync_TwoPhaseFlowBatch_SucceedsWithoutManualStatusOverride` — starts red |
| 3 | ❌ | The single-shot direct-apply path's own `Status`/`AppliedAt`/`RecordCount` behaviour is unchanged after the fix consolidates it | Unit test | Existing `SqliteQuoteImportService` apply-path tests stay green, no assertions changed |
| 4 | ❌ | The test-only `MarkImportBatchAppliedAsync` workaround is removed and no longer needed | Live (review) | `grep -rn "MarkImportBatchAppliedAsync" tests/` returns no results |
| 5 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 6 | ❌ | T1 — app starts in Visual Studio; a manual two-phase decide→apply→reverse cycle works end to end | Live (T1) | Developer to confirm in Visual Studio once implemented |
| 7 | ❌ | T2 — Docker smoke test: reproduce the issue's own repro steps 1-5, confirm step 5 now returns `200` instead of `422` | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + the issue's own curl sequence; add to CLAUDE.md's T2 checklist per this project's living-checklist convention |

---

## Notes

T1 and T2 are both required per this project's blanket rule — this changes the behaviour of the
production apply/reverse code path, not a docs-only or test-only change.

Sequenced first under #217 (parent: "Establish conflict-resolution coverage for every bundled source
file") because that issue's own testing methodology depends on a working resolve→apply→reverse→retry
cycle for iterative conflict-resolution testing — without this fix, no batch staged under `review`
policy (exactly the policy #217's methodology forces on every bundled file) could ever be reversed and
retried.

The issue's own "Failing tests" table and `MarkImportBatchAppliedAsync` reference both name
`Quotinator.Engine.Tests` — stale since #206's project merge. Correct the issue body to
`Quotinator.Core.Tests` during implementation (draft-review-approve, per this project's standing
workflow rule), not silently.
