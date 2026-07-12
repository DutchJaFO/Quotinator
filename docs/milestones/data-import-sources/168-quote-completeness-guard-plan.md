# #168 — Quote's own Modify path never checks CompletenessGuard

**Status:** In progress
**GitHub issue:** #168
**Tiers required:** T1, T2
**Depends on:** #165

---

## Scope changes

Widened during planning (2026-07-12), before any implementation started. The issue was originally
scoped to Quote's own planning path only, mirroring Source's `PlanSourcesAsync` (#162) exactly.
Reading that method as the precedent to copy surfaced that Source's own `ShouldBlock` check computes
its changed-field set from the **raw incoming** value, before the batch's `DuplicateResolutionPolicy`
is applied — so a `Complete` Source can be held (`Blocked`) even under `Skip` policy, whose resolved
write is always the existing value (i.e. nothing would actually change). Copying that into Quote would
propagate the same gap. Decided with the user: fix both entities to gate on the **policy-resolved**
value instead — a row only blocks when the write would actually change it. This issue now also touches
`ImportActionPlanner.PlanSourcesAsync` (previously #162's own code, not yet in a tagged release, so
free to edit) in addition to the Quote path originally scoped. See "What needs to be done" below for
the full, current requirement list.

---

## Spec requirements (from the GitHub issue, as updated)

1. `Sql.Quotes.SelectRawById()` (`Quotinator.Engine/Queries/Sql.cs`) selects `q.CompletenessStatus`;
   `QuoteSeedWriter.RawQuoteRow`/`ExistingQuoteFields` carry it through so the planner can read a
   quote's current completeness status without a second query.
2. `PlanAsync`'s Quote Modify branch (`ImportActionPlanner.cs:94-133`) computes `resolved` (the
   policy-resolved value, already computed today for the write itself) *before* deciding whether to
   block, derives the changed-field set from `resolved` vs `existingFields` (not raw incoming vs
   existing), and calls `CompletenessGuard.ShouldBlock` on that set. When it returns `true`, stages
   `ImportActionStatus.Blocked` instead of the current `Pending`/`Decided` `Modify` action, regardless
   of `DuplicateResolutionPolicy`. The existing "always stage an action per duplicate, even a no-op"
   behaviour (kept for `GET /import/actions` audit-trail honesty, per the comment at
   `ImportActionPlanner.cs:100-103`) is unchanged — this only affects whether the staged action is
   `Blocked` vs `Modify`.
3. `PlanSourcesAsync` (`ImportActionPlanner.cs:262-358`) reordered so `resolved` (the
   `isMerge`/`mergeResult`/policy-switch block, currently computed *after* the `ShouldBlock` check) is
   computed first. The existing raw-incoming-vs-existing diff and its "unchanged — silent reuse"
   early-continue (line 284-286) stay exactly as they are — they gate whether anything is staged at
   all, a separate, unrelated existing behaviour. A **second**, resolved-vs-existing diff is computed
   purely for the `ShouldBlock` argument. Net effect: `Skip` policy can never block a `Complete`
   Source (resolved always equals the existing value); a merge policy only blocks on fields the merge
   itself would actually change, not on every raw textual difference.
4. New test `PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` starts red, ends
   green.
5. New test `PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock` starts red, ends green — proves
   requirement 3's fix (this exact case was previously mis-blocked).
6. Existing test `ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged`
   rewritten to also assert the field overwrite itself was prevented, not only that
   `CompletenessStatus` wasn't reset.

**Dropped during implementation:** `TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch`
(originally requirement 6) was found to be redundant — `TryApplyBatchAsync_BlockedActionInBatch_HoldsEntireBatch`
already exists in `Quotinator.Data.Tests` and is entity-agnostic (the coordinator has no concept of
"Quote" vs "Source"), so it already proves the whole-batch hold for any `Blocked` action regardless of
entity type. Added instead: `PlanAsync_QuoteAlreadyComplete_SkipPolicy_DoesNotBlock`, a regression guard
proving `Skip` policy never blocks a Complete quote (mirrors requirement 5's Source-side test).

---

## Steps

### 1. Write the red tests

**Status:** ✅ Done. `PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` and
`PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock` added to `ImportActionPlannerTests.cs`.
Confirmed red via `git stash` (implementation reverted, tests kept): both failed against pre-fix code
with the expected wrong status (`Decided` instead of `Blocked` for the Quote test; `Blocked` instead
of `Decided` for the Source/Skip test). `git stash pop` restored the fix.

### 2. Surface `CompletenessStatus` on Quote's existing-fields read path

**Status:** ✅ Done. Added `q.CompletenessStatus` to `Sql.Quotes.SelectRawById()`. Added a
`CompletenessStatus` field to `QuoteSeedWriter.RawQuoteRow` and a `CompletenessStatus` property to
`ExistingQuoteFields` (now a 3-field record, up from 2). The `seenQuotes` in-batch cache (for the same
quote id appearing twice in one file) needed a matching `seenQuoteStatus` dictionary added alongside
it, populated the same way — `Incomplete` for a fresh Add, the original DB row's status for a
non-pending Modify — since planning never changes the on-disk status, only apply time does.

### 3. Wire `CompletenessGuard.ShouldBlock` into Quote's Modify planning branch

**Status:** ✅ Done. Moved the `resolved` computation before the blocking decision. When
`ShouldBlock` returns true, stages `Blocked` with `ExistingValue`/`IncomingValue` set, no
`MergedFields` — matching Source's `Blocked` shape exactly.

**Bug found during this step:** the first implementation used a naive `!Equals(a, b)` diff, which
silently over-blocked — `List<string>.Equals` is reference equality, and `genres`' field map value is
rebuilt via `.ToList()` on every round-trip (`QuoteFieldMerge.ToFieldMap`), so two content-identical
genre lists never compared equal, making `Skip` policy appear to always "change" `genres` and
incorrectly block. Caught by `PlanAsync_QuoteAlreadyComplete_SkipPolicy_DoesNotBlock` failing
unexpectedly after the fix was believed complete. Root-caused, then fixed properly: `FieldMergeResolver.ValuesEqual`
(`Quotinator.Data.Import`) already had the correct sequence-aware comparison for this exact reason —
made `public` (was `private`) and reused in all three diff computations in `ImportActionPlanner.cs`
(Quote's new resolved-diff, Source's pre-existing raw-diff, Source's new resolved-diff), rather than
duplicating or re-solving the same problem.

### 4. Fix `PlanSourcesAsync` to gate on the resolved value, not the raw incoming value

**Status:** ✅ Done. Reordered so `isMerge`/`mergeResult`/`resolved` are computed before the
`ShouldBlock` check. Kept the existing raw-diff "unchanged — silent reuse" early-continue as-is (now
using `FieldMergeResolver.ValuesEqual` too, for consistency — harmless today since `SourceActionPayload`
has no list fields, but avoids the same bug class if one is ever added). Computes a second,
resolved-vs-existing diff for `ShouldBlock`.

### 5. Rewrite the misleading existing test

**Status:** ✅ Done. `ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged`
now asserts, in order: the staged action's status is `Blocked` (not `Pending`, which `Review` policy
would otherwise have produced); the quote text in the database is still the pre-import value before
`DecideAsync`/`ApplyBatchAsync` run; and, after an explicit decide + apply, both the new text and the
still-`Complete` status are correct. Full solution build: 0 warnings, 0 errors. Full test suite:
254/254 passing.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A `Complete` quote with a policy-resolved field change stages `Blocked`, not `Modify`/`Pending` | Unit test | `Quotinator.Engine.Tests.PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` — red before (confirmed via `git stash`), green after |
| 2 | ✅ | A `Complete` Source under `Skip` policy never blocks, since `Skip`'s resolved value is always the existing value | Unit test | `Quotinator.Engine.Tests.PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock` — red before (confirmed via `git stash`), green after |
| 3 | ✅ | A `Complete` quote under `Skip` policy never blocks either (regression guard, mirrors requirement 2) | Unit test | `Quotinator.Engine.Tests.PlanAsync_QuoteAlreadyComplete_SkipPolicy_DoesNotBlock` — caught a real `List<string>`-equality bug during implementation (see plan doc step 3), green after the fix |
| 4 | ✅ | The previously-misleading existing test now asserts the overwrite itself is prevented | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged` — rewritten, green |
| 5 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 254/254 passing, 0 warnings, 0 errors |
| 6 | ✅ | Live: importing a change to a `Complete` quote via `POST /api/v1/import` returns held (not a silent success) | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .`: marked a curated Casablanca quote `Complete` via decide+`markCompletenessAs`, then re-imported a changed field — **this is the exact scenario that originally crashed with a bare `500`** (`BuildConflictEntries`'s `InvalidOperationException`, found live, fixed — see step 3 note below); after the fix, returns `202` with `summary.updated: 0`, `pendingActionIds` non-empty, `GET /import/actions?status=Blocked` shows the held action, and the quote text in the running container is confirmed unchanged. The companion Source/`Skip` case (requirement 2) is covered by a passing unit test; a live re-verification of that specific case was attempted but not completed under this session's time budget — not a gap in the fix itself, since #168's unit coverage already proves it |
| 7 | ❌ | App still opens and builds in Visual Studio; this milestone's plan docs remain visible/correct | Live (T1) | Developer starts the app in Visual Studio and confirms no startup error |

---

## Notes

T1 and T2 are both required — per direct developer correction (2026-07-12), T1/T2 are never exempted except for pure documentation-only changes; this issue is a real C# logic change, so both apply regardless of the "no Razor/migration surface" reasoning previously used for #157/#158. See the open process-gap question logged in this session about whether `docs/release-verification.md`'s conditional "When required" wording should be rewritten to state this as a blanket rule.
