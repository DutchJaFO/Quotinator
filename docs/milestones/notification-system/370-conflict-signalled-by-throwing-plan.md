# #370 — An expected import conflict is signalled by throwing, once per conflicted row per render

**Status:** In progress (step 4)
**GitHub issue:** #370
**Tiers required:** T1, T2
**Depends on:** #397

---

## Next action

Execute the steps in order; step 4 is next.

---

## Description

`FieldMergeResolver.ResolveWithDecisions` reports an expected outcome — fields disagree and nobody has
decided — by throwing `UnresolvedFieldConflictException`. Seven call sites catch it as normal flow: six
in `ImportActionPlanner` where a conflict rule covers only some ambiguous fields, and
`SqliteImportActionService.ComputeAmbiguousFields`, which calls the resolver only to read
`ex.FieldNames`. `ComputeAmbiguousFields` runs for every pending row in `GET /import/actions`, so
`/import-review` — which reads that listing and renders twice — throws twice per conflicted row. A
batch export does not call it (step 3).

The decide path throws the same exception for incomplete decisions, alongside three more for conditions
already checked: `ImportActionNotFoundException`, `ImportActionStateException` and
`ImportActionNotDecidableException`. The `DecideImportAction` endpoint and `BulkDecideAsync` catch all
four. `/import-review` decides through `BulkDecideAsync`.

ADR 022 governs the result: a condition the code has already checked is returned as an outcome (rule 1),
and a violated invariant the code built itself still throws (rule 3).

## Impact

1. **It stops the debugger on every occurrence.** With break-on-thrown-exceptions on, a T1 session is
   interrupted by an exception meaning "working as designed" — one measured seeding of a single file
   spanned 54 seconds (19:59:03 → 19:59:57) for that reason.
2. **Every one of these throws is an `Error` line in the container log** since #397, indicating no
   fault — measured on 2026-09-18 as three `UnresolvedFieldConflictException` lines in one run of
   `import-and-staged-actions/01`.

## Decisions

| Source | Governs | Where |
|---|---|---|
| ADR 022 | Checked conditions become outcomes; the planner's self-built invariant stays a throw | Steps 4–6 |
| ADR 016 | The decide outcome is a `Result` type; its cases are an enum in `Quotinator.Data/Enums/` | Step 2 |
| `docs/testing-policy.md` | Global state written once, in `[AssemblyInitialize]` — which is where the thrown-exception recorder subscribes | Step 1 |

## Relation to other issues

- **#409** changes the same two calls — `ComputeAmbiguousFields`' Quote branch and `DecideAsync`'s Quote
  resolution — to pass the case-sensitive field set. Neither blocks the other; it is ordered after this
  issue.
- **#404** (undo's throws) and **#405** (the rest of `BulkDecideAsync`'s catch filter) build on the
  outcome shape this issue introduces. Undo keeps its throws here.

---

## Steps

### 1. A way for a test to see an exception the code caught itself

**Status:** ✅ Done — red against the no-op signatures (the three recording tests failed on their
assertions), green after; `Install()` wired into `Quotinator.Data.Tests` and `Quotinator.Core.Tests`;
build 0 warnings, 0 errors. `Scope_NothingThrown_RecordsNothing` and `Scope_ExceptionThrownOutside_IsNotRecorded`
are controls — they pass against a recorder that records nothing, which is what makes the other three's
red meaningful — so they start green rather than red.

Every "throws nothing" test below asserts on an exception that production code throws and catches
internally, so no assertion on the call can see it — the test would be green before the fix. A
recorder makes it visible:

- `Quotinator.Data.Testing.Diagnostics.ThrownExceptionRecorder`: `Install()` subscribes one handler to
  `AppDomain.FirstChanceException`; `Begin()` opens a scope backed by an `AsyncLocal`, and the scope's
  `Thrown` lists every exception thrown on that logical flow while it is open.
- `Install()` is called from `[AssemblyInitialize]` in `Quotinator.Data.Tests` and
  `Quotinator.Core.Tests` — the one place the testing policy allows global state to be written.
- It records; it never logs, rethrows or swallows, so it changes nothing about the code under test.

Red first like everything else: signatures, then `ThrownExceptionRecorderTests` run red, then the
implementation.

### 2. Add the signatures only

**Status:** ✅ Done — every shape below in place, behaviour unchanged: build 0 warnings, 0 errors, and
`Quotinator.Data.Tests` (1,375), `Quotinator.Core.Tests` (1,685) and `Quotinator.Api.Tests` (950) all
pass. `ResolveEarlyRule` takes the blended existing map already built, so a test can hand it an
inconsistent one — the construction itself stays in `PlanAsync`. Adding `ImportActionPlanner.cs` and
`SqliteImportActionService.cs` to the IDE0090 list surfaced their target-typed `new` warnings, cleared by
`dotnet format` in one pass.

Per `docs/testing-policy.md`'s *Red first means signatures first* — shapes only, no behaviour:

- `FieldMergeResult` gains `IReadOnlyList<string> UnresolvedFields`, always populated. The resolver
  still throws until step 4.
- `ImportActionDecideOutcome` in `Quotinator.Data/Enums/`: `Decided`, `NotFound`, `AlreadyResolved`,
  `NotDecidable`, `UnresolvedFields`.
- `ImportActionDecideResult` in `Quotinator.Data.Import` (ADR 016's `Result`): the outcome, the
  unresolved field names, the action's entity type and its current status, and `Describe()` — the text
  a bulk-decide row error carries for a non-`Decided` outcome.
- `IImportActionCoordinator.DecideAsync` and `IImportActionService.DecideAsync` return
  `Task<ImportActionDecideResult>`; their bodies are unchanged apart from returning `Decided`.
- `ImportActionPlanner.ResolveEarlyRule` — the early-rule resolution moved into an `internal static`
  method taking its inputs, body unchanged, so step 6's invariant can be proven directly.

### 3. Write every test and confirm each is red

**Status:** ✅ Done — 25 unit tests red against step 2's signatures, each for its own reason: the
recorder saw `UnresolvedFieldConflictException` thrown, the outcome was wrong, or the endpoint answered
`204`. Five start green as controls: `ResolveWithDecisions_NothingAmbiguous_ReportsNoUnresolvedFields`,
`ResolveEarlyRule_EveryFieldResolved_ReturnsMergedFields`, `DecideAsync_AllFieldsDecided_ReturnsDecided`
(step 2 already returns `Decided`), `ImportActionDecideResultTests.Describe_Decided_IsEmpty`, and
`ExportBatchAsync_PendingModifyConflicts_ReportsAmbiguousFieldsWithoutThrowing`.

**The export never threw.** `ExportBatchAsync` builds its rows from the stored fields and never calls the
resolver, so the issue's claim that a batch export throws is wrong — its test is a control, and the
canary below confirms it (the export added no line).

**The natural-key Source site needed a different second field.** That branch only ever matches a row
whose date is equal or empty, so a date can never be ambiguous there; the test makes `seriesId` and
`seasonId` differ instead, with the rule covering only `seriesId`.

**One test beyond the issue's table:** `ImportActionDecideResultTests.Describe_EachOutcome_MatchesTheMessageItReplaces`,
the check that every row-error text is the one the exception carried (requirement 4).

**Canary:** `import-and-staged-actions/28` against an image built from `ef561186` — `before=0`,
`after=8`: three `UnresolvedFieldConflictException` from the listing, three from the review page's
render, and the refused decide's twice under one id. Every other expectation held. The run also found
the document reading the export's JSON array as one element in PowerShell 5.1; corrected and re-run.
Container, image and worktree removed.

Every row of the verification checklist, written against step 2's signatures and run red. The tests
that assert today's throws are rewritten, not kept alongside — they encode the behaviour this issue
removes:

- `FieldMergeResolverTests` — the three `…_Throws…` tests.
- `ImportActionResolutionCoordinatorTests` — `DecideAsync_UnknownId_…` and `DecideAsync_AlreadyAppliedAction_…`.
- `SqliteImportActionServiceTests` — `DecideAsync_NonQuoteAction_…`, `DecideAsync_UnknownId_…`,
  `DecideAsync_AmbiguousFieldLeftUndecided_…`.
- `ImportActionNotDecidableExceptionTests` becomes `ImportActionDecideResultTests`, keeping its assertion
  that the not-decidable text names no specific entity type.
- `ImportActionEndpointsTests.DecideAction_*` — `FakeImportActionService` returns results instead of
  throwing, and each test keeps asserting exactly what it asserts today.

The automated document is run against a canary built from the commit before this issue's first code
commit, which already contains #397: it must log one thrown `UnresolvedFieldConflictException` per
conflicted row per read. Container, image and worktree are removed afterwards.

### 4. Report unresolved fields instead of throwing

**Status:** ⬜ Not started

`ResolveWithDecisions` returns the unresolved names in `UnresolvedFields`, and
`UnresolvedFieldConflictException` is deleted. The six planner fall-through sites test
`UnresolvedFields.Count == 0` where they caught the exception, and `ComputeAmbiguousFields` returns
`UnresolvedFields` directly.

The Universe site (`PlanUniverseAsync`) cannot reach the throw: a Universe has one field, so a rule that
decides anything decides everything. It reads the result like the others, and has no red test of its
own.

### 5. Return an outcome from the decide path

**Status:** ⬜ Not started

The coordinator returns `NotFound`/`AlreadyResolved` where it threw. `SqliteImportActionService.DecideAsync`
returns the coordinator's non-`Decided` result unchanged, `NotDecidable` where it threw, and
`UnresolvedFields` after each of its nine resolver calls when any field is left.

`DecideImportAction` maps each outcome to the status and localised message its catch block produces
today. `BulkDecideAsync` reports each non-`Decided` result as a row error carrying `Describe()`; its
catch filter keeps only what #405 converts. `ImportActionNotDecidableException` is deleted.
`ImportActionNotFoundException` and `ImportActionStateException` stay, since undo still throws them
until #404.

### 6. Keep the planner's one invariant throw

**Status:** ⬜ Not started

`ResolveEarlyRule` supplies both sides of every undecided field itself, so no field can be left
unresolved there. An unresolved field would be a bug in that construction, which no caller can respond
to — ADR 022 rule 3 — so it throws `InvalidOperationException`, the one throw this issue keeps.

### 7. Build and run the full suite

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 8. T2 pass

**Status:** ⬜ Not started

Against a fresh build of the branch: the designated smoke set, this issue's
`import-and-staged-actions/28-review-throws-nothing.md`, and the documents that list or decide staged
actions — `import-and-staged-actions/01`, `/20`, `/25`, `/26` and `/27`. Each container's log is read for
`[Runtime - Exception]` lines before it is removed, and every line is triaged.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A test can see an exception the code threw and caught itself | Unit test | `ThrownExceptionRecorderTests.Scope_ExceptionThrownAndCaughtInside_IsRecorded`, `…Scope_NothingThrown_RecordsNothing`, `…Scope_ExceptionThrownOutside_IsNotRecorded`, `…Scope_ExceptionThrownAfterAnAwait_IsRecorded`, `…Scopes_OnConcurrentFlows_RecordOnlyTheirOwn` |
| 2 | ❌ | The resolver reports unresolved fields instead of throwing | Unit test | `FieldMergeResolverTests.ResolveWithDecisions_AmbiguousFieldNoDecision_ReportsFieldName`, `…_AmbiguousFieldsNoDecision_ReportsEveryAmbiguousFieldName`, `…_FieldInCaseSensitiveSet_DiffersOnlyByCase_ReportsFieldName`, `…_NothingAmbiguous_ReportsNoUnresolvedFields`, `…_AmbiguousFieldNoDecision_ThrowsNothing` |
| 3 | ❌ | `UnresolvedFieldConflictException` and `ImportActionNotDecidableException` no longer exist | Live | `git grep -n -e UnresolvedFieldConflictException -e ImportActionNotDecidableException -- src tests` prints nothing and exits 1 |
| 4 | ❌ | The planner's fall-through sites stage Pending and throw nothing | Unit test | `ImportActionPlannerTests.PlanAsync_ReviewPolicy_RuleCoversOnlySomeChangedFields_StagesPendingWithoutThrowing`, `…PlanSourcesAsync_RuleCoversOnlySomeChangedFields_StagesPendingWithoutThrowing`, `…PlanSourcesAsync_ByNaturalKey_RuleCoversOnlySomeChangedFields_StagesPendingWithoutThrowing`, `…PlanSeriesAsync_RuleCoversOnlySomeChangedFields_StagesPendingWithoutThrowing`, `…PlanSeasonsAsync_RuleCoversOnlySomeChangedFields_StagesPendingWithoutThrowing` |
| 5 | ❌ | Listing and exporting report ambiguous fields and throw nothing | Unit test | `SqliteImportActionServiceTests.GetPagedAsync_PendingModifyConflicts_ReportsAmbiguousFieldsWithoutThrowing`, `…ExportBatchAsync_PendingModifyConflicts_ReportsAmbiguousFieldsWithoutThrowing` |
| 6 | ❌ | The coordinator's decide returns not-found and already-resolved, throwing nothing | Unit test | `ImportActionResolutionCoordinatorTests.DecideAsync_UnknownId_ReturnsNotFoundWithoutThrowing`, `…DecideAsync_AlreadyAppliedAction_ReturnsAlreadyResolvedWithoutThrowing` |
| 7 | ❌ | The service's decide returns every outcome, throwing nothing | Unit test | `SqliteImportActionServiceTests.DecideAsync_UnknownId_ReturnsNotFoundWithoutThrowing`, `…DecideAsync_AlreadyAppliedAction_ReturnsAlreadyResolvedWithoutThrowing`, `…DecideAsync_NonQuoteAction_ReturnsNotDecidableWithoutThrowing`, `…DecideAsync_AmbiguousFieldLeftUndecided_ReturnsUnresolvedFieldNamesWithoutThrowing`, `…DecideAsync_AllFieldsDecided_ReturnsDecided` |
| 8 | ❌ | The endpoint's responses and the bulk-decide row errors are unchanged | Unit test | `ImportActionEndpointsTests.DecideAction_UnknownId_Returns404`, `…DecideAction_AmbiguousFieldUnresolved_Returns422WithFieldNames`, `…DecideAction_AlreadyResolved_Returns422`, `…DecideAction_NotDecidable_Returns422`, `SqliteImportActionServiceTests.BulkDecideAsync_AmbiguousFieldLeftUndecided_ReportedAsRowErrorWithoutThrowing`, `ImportActionDecideResultTests.Describe_NotDecidable_DoesNotNameASpecificEntityType` |
| 9 | ❌ | The planner's early-rule invariant throws when violated, and only then | Unit test | `ImportActionPlannerTests.ResolveEarlyRule_FieldLeftUnresolved_ThrowsInvalidOperationException`, `…ResolveEarlyRule_EveryFieldResolved_ReturnsMergedFields` |
| 10 | ❌ | Listing, exporting, rendering the review page and an undecided decide throw nothing in a running container, while every conflicted row still reports its ambiguous fields | Live (T2) | `automated-testing/import-and-staged-actions/28-review-throws-nothing.md` passes on this branch's build, and fails on the canary from step 3 with one thrown line per conflicted row per read |
| 11 | ❌ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |

### The automated document

`import-and-staged-actions/28-review-throws-nothing.md` owns its input and never stops its container:

1. A container with `Quotinator__IncludeDefaultSources=false`, so nothing is seeded that the test did
   not write.
2. Import a base file of three quotes, then a file re-stating the same three ids with different text
   under `review` — three Pending conflicts.
3. Count `[Runtime - Exception]` lines: zero.
4. List the batch's pending actions — every row reports `quoteText` as ambiguous (the positive control:
   the code path ran). Export the batch. `GET /import-review`, whose server render reads the same
   listing. `POST …/decide` with `{}` for one row — `422` naming `quoteText`.
5. Count again: still zero.
