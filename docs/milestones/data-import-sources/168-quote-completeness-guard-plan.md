# #168 — Quote's own Modify path never checks CompletenessGuard

**Status:** Planning
**GitHub issue:** #168
**Tiers required:** T2
**Depends on:** #165

---

## Spec requirements (from the GitHub issue)

1. `PlanAsync`'s Quote Modify branch (`ImportActionPlanner.cs:94-133`) calls `CompletenessGuard.ShouldBlock` before staging a Modify action, and stages `Blocked` instead when it returns `true` — mirroring Source's id-matched path (`ImportActionPlanner.cs:289`).
2. New test `PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` starts red, ends green.
3. New test `TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch` starts red, ends green — confirms the whole-batch hold (#165) applies to a `Blocked` Quote action the same way it already does for Source.
4. Existing test `ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged` rewritten to also assert the field overwrite itself was prevented, not only that `CompletenessStatus` wasn't reset.

---

## Steps

### 1. Write the red tests

**Status:** Not started.

Add `PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` and `TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch` to `Quotinator.Engine.Tests`. Confirm both fail against current code before touching `ImportActionPlanner.cs`.

### 2. Wire `CompletenessGuard.ShouldBlock` into Quote's Modify planning branch

**Status:** Not started.

In `PlanAsync`'s per-quote loop (`ImportActionPlanner.cs:94-133`), before staging a `Modify`/`Pending` action, compute the changed-field set between `existingFields` and `incomingFields` and call `CompletenessGuard.ShouldBlock(existingRow.CompletenessStatus, changedFields)`. When it returns `true`, stage `ImportActionStatus.Blocked` instead of the current `Pending`/`Decided` logic, regardless of `DuplicateResolutionPolicy`.

### 3. Rewrite the misleading existing test

**Status:** Not started.

Update `ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged` (`SqliteImportActionServiceTests.cs:177-201`) so it also asserts the quote text was not overwritten (or that the action staged as `Blocked` rather than reaching `Decided`/apply at all), not only that `CompletenessStatus` remained `Complete`.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A `Complete` quote with a changed field stages `Blocked`, not `Modify`/`Pending` | Unit test | `Quotinator.Engine.Tests.PlanAsync_QuoteAlreadyComplete_ChangedFields_StagesBlockedNotModify` — red before, green after |
| 2 | ❌ | A `Blocked` Quote action holds the rest of its batch until resolved | Unit test | `Quotinator.Engine.Tests.TryApplyBatchAsync_BlockedQuoteInBatch_HoldsUnrelatedActionsInSameBatch` — red before, green after |
| 3 | ❌ | The previously-misleading existing test now asserts the overwrite itself is prevented | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.ApplyBatchAsync_QuoteAlreadyComplete_MarkCompletenessAsOmitted_StatusUnchanged` — rewritten, green |
| 4 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite passes, 0 warnings |
| 5 | ❌ | Live: importing a change to a `Complete` quote via `POST /api/v1/import` returns `202`/held, not a silent `200` | Live (T2) | Docker smoke test against a curated quote marked `Complete`, re-imported with a changed field — batch shows a `Blocked` action via `GET /api/v1/import/actions`, and the quote text is unchanged until decided |

---

## Notes

T1 not required — this is a backend staging-engine logic change with no Razor/UI surface, same reasoning as #157/#158's T1 exemption. T2 is required because the fix changes observable behaviour of `POST /api/v1/import`, not just internal code organisation.
