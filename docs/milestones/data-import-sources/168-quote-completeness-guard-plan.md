# #168 — Quote's own Modify path never checks CompletenessGuard

**Status:** Planning
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
6. New test `TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch` starts red, ends
   green — confirms the whole-batch hold (#165) applies to a `Blocked` Quote action the same way it
   already does for Source.
7. Existing test `ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged`
   rewritten to also assert the field overwrite itself was prevented, not only that
   `CompletenessStatus` wasn't reset.

---

## Steps

### 1. Write the red tests

**Status:** Not started.

Add `PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify`,
`PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock`, and
`TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch` to
`Quotinator.Engine.Tests`. Confirm all three fail against current code before touching
`ImportActionPlanner.cs`.

### 2. Surface `CompletenessStatus` on Quote's existing-fields read path

**Status:** Not started.

Add `q.CompletenessStatus` to `Sql.Quotes.SelectRawById()`'s `SELECT` list. Add a
`CompletenessStatus` field to `QuoteSeedWriter.RawQuoteRow` and a `CompletenessStatus` property to
`ExistingQuoteFields`, populated from the row.

### 3. Wire `CompletenessGuard.ShouldBlock` into Quote's Modify planning branch

**Status:** Not started.

In `PlanAsync`'s per-quote loop, move the `resolved` computation (currently after the
`actions.Add(...)` for Modify) earlier so it runs before the blocking decision. Derive
`changedFields` from `resolved` vs `existingFields` (not `incomingFields` vs `existingFields`). Call
`CompletenessGuard.ShouldBlock`; when true, stage `Blocked` with `ExistingValue`/`IncomingValue` set
(no `MergedFields`, matching Source's `Blocked` shape) instead of the current `Modify` staging logic.

### 4. Fix `PlanSourcesAsync` to gate on the resolved value, not the raw incoming value

**Status:** Not started.

Reorder so `isMerge`/`mergeResult`/`resolved` (currently lines 305-313) are computed before the
`ShouldBlock` check (currently lines 284-303). Keep the existing raw-diff "unchanged — silent reuse"
early-continue as-is. Compute a second, resolved-vs-existing diff and pass that to `ShouldBlock`
instead of the raw diff.

### 5. Rewrite the misleading existing test

**Status:** Not started.

Update `ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged`
(`SqliteImportActionServiceTests.cs:177-201`) so it also asserts the quote text was not overwritten
(or that the action staged as `Blocked` rather than reaching `Decided`/apply at all), not only that
`CompletenessStatus` remained `Complete`.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A `Complete` quote with a policy-resolved field change stages `Blocked`, not `Modify`/`Pending` | Unit test | `Quotinator.Engine.Tests.PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` — red before, green after |
| 2 | ❌ | A `Complete` Source under `Skip` policy never blocks, since `Skip`'s resolved value is always the existing value | Unit test | `Quotinator.Engine.Tests.PlanSourcesAsync_CompleteSource_SkipPolicy_DoesNotBlock` — red before, green after |
| 3 | ❌ | A `Blocked` Quote action holds the rest of its batch until resolved | Unit test | `Quotinator.Engine.Tests.TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch` — red before, green after |
| 4 | ❌ | The previously-misleading existing test now asserts the overwrite itself is prevented | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged` — rewritten, green |
| 5 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite passes, 0 warnings |
| 6 | ❌ | Live: importing a change to a `Complete` quote via `POST /api/v1/import` returns held (not a silent success), and a `Complete` Source under `Skip` policy does not block | Live (T2) | Docker smoke test: (a) re-import a curated quote marked `Complete` with a changed field — batch shows a `Blocked` action via `GET /api/v1/import/actions`, quote text unchanged until decided; (b) re-import a curated source marked `Complete` with drifted title under `duplicateResolution.default=skip` — batch applies cleanly with no `Blocked` action |
| 7 | ❌ | App still opens and builds in Visual Studio; this milestone's plan docs remain visible/correct | Live (T1) | Developer starts the app in Visual Studio and confirms no startup error |

---

## Notes

T1 and T2 are both required — per direct developer correction (2026-07-12), T1/T2 are never exempted except for pure documentation-only changes; this issue is a real C# logic change, so both apply regardless of the "no Razor/migration surface" reasoning previously used for #157/#158. See the open process-gap question logged in this session about whether `docs/release-verification.md`'s conditional "When required" wording should be rewritten to state this as a blanket rule.
