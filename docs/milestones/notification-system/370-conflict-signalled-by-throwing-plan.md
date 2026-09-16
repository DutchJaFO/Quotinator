# #370 — An expected import conflict is signalled by throwing, once per conflicted row per render

**Status:** Planning
**GitHub issue:** #370
**Tiers required:** T1, T2
**Depends on:** #397

---

## Next action

Execute this plan once #397 reaches `Waiting for release`. Every design decision is settled below; the
dependency is the only thing holding it, because step 2's automated document can only go red against a
build that already logs thrown exceptions.

---

## Description

`FieldMergeResolver.ResolveWithDecisions` reports an expected outcome — fields disagree and nobody has
decided — by throwing `UnresolvedFieldConflictException`. Seven call sites catch it as normal flow: six
in `ImportActionPlanner` where a conflict rule covers only some ambiguous fields, and
`SqliteImportActionService.ComputeAmbiguousFields`, which calls the resolver only to read
`ex.FieldNames`. `ComputeAmbiguousFields` runs for every pending row in `GET /import/actions` and in a
batch export, so `/import-review` — which reads that listing and renders twice — throws twice per
conflicted row.

The decide path throws the same exception for incomplete decisions, alongside three more for conditions
already checked: `ImportActionNotFoundException`, `ImportActionStateException` and
`ImportActionNotDecidableException`. The `DecideImportAction` endpoint and `BulkDecideAsync` catch all
four together.

Under the rule #398 records — throw only when a condition cannot be detected any other way, and catch at
the first point a response can be formed — none of these is a throw. The one throw kept is an invariant
violation no caller can respond to (step 5).

## Impact

1. **It stops the debugger on every occurrence.** With break-on-thrown-exceptions on, a T1 session is
   interrupted by an exception meaning "working as designed" — the measured run shows one file's seeding
   spanning 54 seconds (19:59:03 → 19:59:57) for that reason.
2. **Once #397 lands, every one of these throws is an `Error` line in the container log** indicating no
   fault, which is the noise that trains a reader to skip exception lines.

**Recorded because the first triage got it wrong.** #303's plan called this "expected, not a fault …
recorded rather than chased", on the grounds that the application still functions. "No impact" was
asserted without measuring; the impact lands on whoever is debugging.

---

## Steps

### 1. Add the signatures only

**Status:** ⬜ Not started

Per `docs/testing-policy.md`'s *Red first means signatures first* — shapes only, no behaviour, so every
test in step 2 fails on its assertion rather than on the build:

- `FieldMergeResult` gains `IReadOnlyList<string> UnresolvedFields`, always populated (empty when nothing
  is unresolved). `ResolveWithDecisions` keeps its name and parameters; it still throws until step 3.
- `ImportActionDecideOutcome` enum in `Quotinator.Data/Enums/` (ADR 016): `Decided`, `NotFound`,
  `AlreadyResolved`, `NotDecidable`, `UnresolvedFields`.
- `ImportActionDecideResult` record in `Quotinator.Data.Import` carrying the outcome, the unresolved field
  names, the action's entity type (for the not-decidable message), and the action's current status (for
  the already-resolved message). A service return value, not a boundary type, so no ADR 016 suffix.
- `IImportActionResolutionCoordinator.DecideAsync` and `IImportActionService.DecideAsync` return
  `Task<ImportActionDecideResult>`, their bodies unchanged apart from returning `Decided`.

### 2. Write every test and confirm each is red

**Status:** ⬜ Not started

Every row of the Verification checklist below, written against step 1's signatures and run red. The four
`FieldMergeResolverTests` that asserted the throw are rewritten as the `Reports…` tests rather than kept
alongside them — they encode the behaviour this issue removes.

The existing `DecideAction_*` endpoint tests and `FakeImportActionService` are rewired to return results
instead of throwing, and must still assert exactly what they assert today.

The automated document is run against a canary: `git worktree add` the commit before this issue's first
code commit, with #397 already merged into it, `docker build` it under a distinct tag, run the document,
confirm it logs one thrown `UnresolvedFieldConflictException` per conflicted row, then remove the
container, image and worktree.

### 3. Report unresolved fields instead of throwing

**Status:** ⬜ Not started

`ResolveWithDecisions` returns the unresolved names in `UnresolvedFields`. `UnresolvedFieldConflictException`
is deleted. The six planner fall-through sites test `UnresolvedFields.Count == 0` where they caught the
exception, and `ComputeAmbiguousFields` returns `UnresolvedFields` directly.

### 4. Return an outcome from the decide path

**Status:** ⬜ Not started

The coordinator returns `NotFound`/`AlreadyResolved` where it threw. `SqliteImportActionService.DecideAsync`
returns the coordinator's non-`Decided` result unchanged, `NotDecidable` where it threw, and
`UnresolvedFields` after each of its nine resolver calls when any field is left.

The response texts do not change. `DecideImportAction` maps each outcome to the status and localised
message its catch block produces today. `BulkDecideAsync`'s row errors currently carry each exception's
`Message`, so those four English strings move into one mapping from `ImportActionDecideResult` to text,
used by `BulkDecideAsync` alone — the exception constructors that held them are deleted with the throws.

`UndoDecisionAsync` shares the coordinator's checks but keeps its throws: it belongs to #398's
batch-lifecycle sub-issue.

### 5. Keep the planner's one invariant throw, and make it provable

**Status:** ⬜ Not started

The early-rule resolution in `ImportActionPlanner` (the `blendedExisting` call) supplies both sides of
every undecided field itself, so no field can be left unresolved there. An unresolved field would be a bug
in that construction, which no caller can respond to — the one case that still throws, as
`InvalidOperationException`.

Normal planning cannot reach it, so the resolution moves into an `internal static` method taking its
inputs, and the check is proven against that method directly — the same "a private method is unprovable"
reason #303 gives for `ImportReview.DecideAndApplyAsync`.

### 6. Build and run the full suite

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 7. T2 pass

**Status:** ⬜ Not started

The designated smoke set, plus the review workflow document
(`import-and-staged-actions/01-staged-action-review-workflow.md`) and this issue's own
`import-and-staged-actions/28-review-throws-nothing.md`, against a fresh build of the branch.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The resolver reports unresolved fields instead of throwing | Unit test | `FieldMergeResolverTests.ResolveWithDecisions_AmbiguousFieldNoDecision_ReportsFieldName`, `…_AmbiguousFieldsNoDecision_ReportsEveryAmbiguousFieldName`, `…_FieldInCaseSensitiveSet_DiffersOnlyByCase_ReportsFieldName`, `…_NothingAmbiguous_ReportsNoUnresolvedFields` |
| 2 | ❌ | `UnresolvedFieldConflictException` no longer exists | Live | `git grep -n UnresolvedFieldConflictException -- src tests` prints nothing and exits 1 |
| 3 | ❌ | The planner's fall-through sites and `ComputeAmbiguousFields` read the result, throwing nothing | Unit test | `ImportActionPlannerTests.Plan_RuleCoversOnlySomeAmbiguousFields_StagesPendingAndThrowsNoException`, `SqliteImportActionServiceTests.GetPagedAsync_PendingModifyConflicts_ThrowsNoException`, `SqliteImportActionServiceTests.ExportBatchAsync_PendingModifyConflicts_ThrowsNoException` |
| 4 | ❌ | The coordinator's decide returns not-found and already-resolved outcomes | Unit test | `ImportActionResolutionCoordinatorTests.DecideAsync_UnknownId_ReturnsNotFound`, `ImportActionResolutionCoordinatorTests.DecideAsync_AlreadyAppliedAction_ReturnsAlreadyResolved` |
| 5 | ❌ | The service's decide returns every outcome, throwing nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_UnknownId_ReturnsNotFound`, `…DecideAsync_NonQuoteAction_ReturnsNotDecidable`, `…DecideAsync_AmbiguousFieldLeftUndecided_ReturnsUnresolvedFieldNames`, `…DecideAsync_AmbiguousFieldLeftUndecided_ThrowsNoException` |
| 6 | ❌ | The endpoint's responses and the bulk-decide row errors are unchanged | Unit test | `ImportActionEndpointsTests.DecideAction_UnknownId_Returns404`, `…DecideAction_AmbiguousFieldUnresolved_Returns422WithFieldNames`, `…DecideAction_AlreadyResolved_Returns422`, `…DecideAction_NotDecidable_Returns422`, `SqliteImportActionServiceTests.BulkDecideAsync_AmbiguousFieldLeftUndecided_ReportedAsRowError` |
| 7 | ❌ | The planner's early-rule invariant still throws when violated, and only then | Unit test | `ImportActionPlannerTests.ResolveEarlyRule_FieldLeftUnresolved_ThrowsInvalidOperationException`, `ImportActionPlannerTests.ResolveEarlyRule_EveryFieldResolved_ReturnsMergedFields` |
| 8 | ❌ | Listing, exporting and an undecided decide throw nothing in a running container, while every conflicted row still reports its ambiguous fields | Live (T2) | `automated-testing/import-and-staged-actions/28-review-throws-nothing.md` passes on this branch's build, and fails on the canary from step 2 with one thrown-exception line per conflicted row |
| 9 | ❌ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |
