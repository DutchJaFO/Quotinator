# #389 — A staged review batch cannot be discarded once the planner has staged a no-op in it as Applied

**Status:** Waiting for release
**GitHub issue:** #389
**Tiers required:** T1, T2
**Depends on:** #373, #376, #377

---

## Description

`POST /api/v1/import/actions/discard?batchId=` refuses any batch containing an `Applied` action. Since
#373 the planner stages no-op actions straight to `Applied` at plan time — `Unchanged` (#373),
`ResolvedToExisting` (#377), `AlreadyReported` (#376) — so a review batch whose file restates anything
already stored carries one beside its `Pending` conflicts, and every such batch is now impossible to
discard. Found by #369's T2 step 5, where the review page's Dismiss hit the same refusal; reproduced
through the REST endpoint against the same batch (422, action left `Pending`).

The guard dates from #154 (2026-07-07), when `Applied` could only mean a real apply had happened —
`TryApplyBatchAsync` applies a batch atomically. `ImportActionKind`'s documentation says how *apply*
treats a staged-`Applied` no-op and says nothing about *discard*.

## Cross-check against authoritative sources, 2026-09-10

Per `docs/workflow/process.md`'s Planning step 3, in its stated order.

### Architecture decisions

1. **ADR 014** — discarding marks actions `Discarded`; nothing is purged, and the no-op rows this issue
   leaves alone stay as they were recorded. No conflict.
2. **ADR 016** — the classification is an extension over an enum, so it lives in `Quotinator.Data/Enums/`,
   as `SeedBatchOriginExtensions` already does.
3. **ADR 008** — no enum member is added and no column changes, so no CHECK constraint moves.
4. **ADR 012 / `CLAUDE.md`'s case-insensitivity rule** — `Import_Action.Status` is compared through
   `TextClauses.Equals`, never a hand-written comparison; `SqlTextCaseGuard` lists it explicitly.

### JSON schemas, generators and scripts

5. **Neither engages.** No file input, no generator.

### C# models and project documentation

6. **`Sql.SystemImportActions.MarkBatchDiscarded` updates every row sharing the batch id.** Changing only
   the guard would therefore stamp the `Applied` no-ops `Discarded` too, contradicting the issue's first
   expected behaviour. The statement itself has to leave terminal rows untouched.
7. **`docs/api-endpoints.md`** describes discard as covering "every staged action in a batch", and the
   endpoint's own description says the same. Both change.
8. **T2 `import-and-staged-actions/04-discard.md`** previews the already-seeded curated file under
   `review` and expects `204`. Since #373 that preview can stage `Unchanged` no-ops rather than `Pending`
   actions, which would make the document fail today for the reason this issue fixes — or stage nothing
   awaiting a decision at all. Which of those it is gets measured in step 5, not assumed.

## Design (developer decisions, 2026-09-10)

1. **A new issue, fixed now**, which #369 depends on — rather than folding the fix into #369 or reopening
   #373: the REST behaviour change stays traceable to its own issue.
2. **Only the no-op kinds are exempt.** An `Applied` action is skipped only when its kind is a plan-time
   no-op; any other `Applied` action still refuses the discard, exactly as today.
3. **The no-op set is declared once, exhaustively, and guarded.** Every `ImportActionKind` member is
   classified explicitly, with no fall-through arm for a named member, and a test walks the enum so a kind
   added later fails until someone decides which side it is on — the same pattern as
   `NotificationTable.LayoutFor` and `EveryMetadataKind_HasALayout`.

---

## Steps

**Red and green are per step**, and steps run strictly in order — the same rule as #369's plan.

### 1. Write every test first, and run them red

**Status:** ✅ Done — rows 1, 4 and 5 red on their own assertions; controls 2, 3, 6 and 7 green

Signatures only, returning today's behaviour: `ImportActionKindExtensions.IsPlanTimeNoOp` classifying
nothing. The tests collect unclassified kinds rather than letting an exception escape, so each fails on
its own assertion.

**Result.** Row 1 fails on exactly the reported refusal — *"Batch 'BATCH-1' has already been applied and
cannot be discarded"* — and rows 4 and 5 on their assertions. Rows 2 and 3 pass as the controls they are,
as do the existing discard tests (row 7) and both guard classes (row 6). Build: 0 warnings, 0 errors.

**The first red run was not valid, and the reason is recorded rather than absorbed.** Rows 1 and 3 failed
before reaching the discard at all: `CHECK constraint failed: ActionType IN ('Add', 'Modify')`, raised
while writing the fixture's no-op row. The coordinator tests built `Import_Action` from a hand-written
copy of its DDL that had drifted three migrations behind the real table — no `Unchanged`,
`ResolvedToExisting` or `AlreadyReported`, and no `Stale` status. That fixture now builds from
`CurrentSchema.ApplyDataSchemaAsync`, the schema the application actually creates, and every existing test
in the class passes on it unchanged. A drifted copy is precisely what `CurrentSchema` exists to replace;
it had also been hiding the defect, since no test in this class could stage the row the planner now writes
routinely.

### 2. Declare the plan-time no-op set

**Status:** ✅ Done — rows 4 and 5 green; row 1 still red, as it must be until step 3

`ImportActionKindExtensions.IsPlanTimeNoOp` in `src/Quotinator.Data/Enums/`: `Unchanged`,
`ResolvedToExisting` and `AlreadyReported` are no-ops; `Add` and `Modify` are not. Every member listed;
an unnamed value throws. `ImportActionKind`'s own documentation points at it.

**Result.** 29 run, 28 passed; the one failure is row 1, still on the same refusal, since nothing yet
consults the classification.

### 3. Discard only what still awaits a decision

**Status:** ✅ Done — rows 1, 2, 3, 6 and 7 green

`DiscardBatchAsync` refuses a batch holding an `Applied` action that is not a no-op, and a batch with
nothing left to discard; otherwise it marks `Discarded` every action that is neither `Applied` nor already
`Discarded`. `MarkBatchDiscarded` gains the matching condition, through `TextClauses.Equals`.

**Result.** Build 0 warnings, 0 errors; `Quotinator.Data.Tests` 665/665 across the coordinator, the
writer, both SQL guard classes and the classification, and `Quotinator.Core.Tests`' import-action tests
328/328. An `Applied` action whose kind cannot be read counts as real work and still refuses — the side a
wrong guess can be recovered from. `IImportActionCoordinator`, `IImportActionWriter`,
`IImportActionService` and `ImportBatchStateException` each described discard as covering every action,
and now say what it does.

### 4. Say what discard now does

**Status:** ✅ Done

`docs/api-endpoints.md` and the endpoint's `WithDescription`.

**Result.** Both now say that discard covers what still awaits a decision, that a no-op recorded as applied
stays as recorded, and every `422` case including the new *nothing left awaiting a decision*. The
`WithSummary` said "every staged action" too and changed with them; `WithName` — the `operationId` — did
not. Build 0 warnings, 0 errors; no test pinned the old wording.

### 5. Re-run the live documents that discard

**Status:** ✅ Done — rows 8 and 9 green; each document red against the pre-fix build

`04-discard.md` in full, `20-pending-review-alert.md` step 5, and #369's `27-orphaned-review-row-offers-only-dismiss.md`
steps 5–6 — the last owned by #369, but unrunnable until this lands.

**`20-pending-review-alert.md`, steps 1–5 — pass.** On this build the discard answered `204`, the alert
reads `isDismissed=True dismissReason=resolved`, the `Quote Modify` went `Pending` → `Discarded` and the
`Source Unchanged` stayed `Applied`. Run red first against the build this issue started from
(`quotinator:canary389`, the #369 image, in a container of its own): the same step answered `422`, left
the alert active and the action `Pending` — the defect, reproduced through the document that should have
caught it.

One instrument defect surfaced, and was fixed at the developer's direction: step 1's
`active alerts = $((Get-ActiveReviewAlerts).Count)` printed blank for a single alert — PowerShell 5.1
unrolls the one-element array on return, which the document's own *Determinism* warns about — and step 6
repeated the line. Both now re-wrap in `@(...)`. Shown under PowerShell 5.1.26100 against a stand-in
function returning one, none and two items: the old form prints nothing for one, the new form `1`, `0`
and `2`. Step 2 found the alert, so the run itself was sound; the line could not show it.

**`04-discard.md` — failed as written, rewritten, and now passes.** The cross-check's second possibility
was the real one: previewing the already-seeded curated file stages 37 actions, all `Unchanged` and
`Applied`, and nothing awaiting a decision, so its discard answered `422` on the fixed build as well —
correctly, since there is nothing to discard. The document had been unable to pass since #373, whatever
this issue did. It now previews the shared conflict fixture (`stage-import-conflict.csx`), which stages a
`Quote Modify Pending` beside a `Source Unchanged Applied`, and step 3 stops unless the batch holds both,
since only a batch holding both tells a correct discard from the two wrong ones. Its step 5 follows each
action by `id`. On this build: `204`, `awaiting now Discarded = 1 of 1`, `no-ops still Applied = 1 of 1`,
the quote count unchanged. Against `quotinator:canary389`: `422` and `0 of 1` — recorded in the
document's own canary table. The index row's title changed with it.

**`27-orphaned-review-row-offers-only-dismiss.md`, steps 5–6 — pass, as part of a full re-run.** The
container left over from #369's run held the #369 image, so the document ran again from step 1 in a fresh
one; steps 1–4 gave the same values as before. Step 5's Dismiss left the page reading *Nothing is waiting
for review.* (screenshot), then `pending actions = 0`, the `Quote Modify` `Discarded`, the
`Source Unchanged` still `Applied` and the alert `resolved`. Step 6, under **All**, showed the 8 rows the
API reports, each badge a word — the resolved alert **Done**, the rest **Active** (screenshot, and the
page's own badge text read back). Step 5's expectation had said every action ends `Discarded`, which
predates this issue; it now names both rows and why the no-op stays.

### 6. Boyscout pass over every file this issue touched

**Status:** ✅ Done — every verification row green

Per `docs/workflow/checklist.md`'s closing item.

**Result.** A non-incremental Release build reports 0 warnings, 0 errors, and the full suite (`-m:1`) is
green in every project: Api 929, Changelog 42, Constants 2, Converters 11 + 16 + 9, Core 1,685, Data
1,375, Logging 5, DbInspector 16 — 4,090 tests, none failed or skipped; identical when re-run after
the fixes below.

- **`.editorconfig`**: nine files new to the IDE0008 list and ten to the IDE0090 list — every source file
  this issue created or touched that was not already on them. Escalating exposed 36 `var` declarations,
  33 in `ImportEndpoints.cs` and 3 in `ImportActionWriter.cs`; `dotnet format` cleared them, and a second
  pass cleared the 12 follow-on `IDE0028`/`IDE0090` warnings the explicit types exposed in
  `ImportEndpoints.cs`. The diff was read line by line: explicit types where `var` stood and `[]`/`new()`
  where a type was repeated, nothing else.
- **Log prefixes**: the only log lines in a touched file are `ImportEndpoints.cs`'s two, and both
  templates carry `[Api - Import]`.
- **`nameof` for column names**: `Sql.SystemImportActions` was already converted under #369, and the new
  condition uses it too.
- **Enum placement, file placement, code-behind**: no breach in any touched file.
- **Fixed at the developer's direction, having first been surfaced.** `ImportActionSummaryResponse`, a
  file this issue had not touched, listed values by hand in three summaries and all three had gone stale:
  two action kinds of five, four entity types of ten, four statuses of six. Each now names its source of
  truth — `ImportActionKind`, `ImportActionEntityTypes.All`, `ImportActionStatus` — instead of a copy of
  it, and the class summary names `Import_Action` rather than the table's pre-rename name. The same stale
  entity list sat in two files this issue did touch: `docs/api-endpoints.md`'s action listing, now worded
  generally, and `GET /import/actions`' own description, which now builds its list from
  `ImportActionEntityTypes.All` so it cannot drift again. `IImportActionService`'s summaries had both
  faults too and got both corrections. `ImportActionSummaryResponse.cs` joined both `.editorconfig`
  lists and exposed no warnings. The doc-20 count is recorded under step 5.

**T1, 2026-09-10 — passed.** The developer started the app in Visual Studio from the finished tree:
`1.9.0-alpha`, both schemas up to date (data v22, app v9), 795 quotes, *Quotinator ready*, and no
warning or error line anywhere in the startup log.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A batch with a plan-time no-op and a `Pending` action discards the `Pending` one and leaves the no-op `Applied` | Unit test | `ImportActionResolutionCoordinatorTests.DiscardBatchAsync_BatchWithPlanTimeNoOp_DiscardsTheRestAndKeepsTheNoOp` |
| 2 | ✅ | A batch holding an applied `Modify` is still refused | Unit test | `ImportActionResolutionCoordinatorTests.DiscardBatchAsync_BatchWithAppliedModify_StillThrows` — control |
| 3 | ✅ | A batch holding nothing but no-ops is refused — there is nothing to discard | Unit test | `ImportActionResolutionCoordinatorTests.DiscardBatchAsync_BatchOfOnlyPlanTimeNoOps_Throws` — control |
| 4 | ✅ | Every `ImportActionKind` member is classified, so a new kind fails until it is | Unit test | `ImportActionKindExtensionsTests.EveryKind_IsClassified` |
| 5 | ✅ | Exactly the three kinds the planner stages as `Applied` are no-ops | Unit test | `ImportActionKindExtensionsTests.IsPlanTimeNoOp_IsExactlyTheKindsStagedAsApplied` |
| 6 | ✅ | The changed statement passes the SQL guards | Unit test | `SqlQueryGuardTests` — existing enumeration; control |
| 7 | ✅ | Existing discard behaviour is unchanged for a batch with no no-ops | Unit test | `ImportActionResolutionCoordinatorTests.DiscardBatchAsync_StagedBatch_MarksEveryActionDiscardedWithoutTouchingDomainTables` and `SqliteImportActionServiceTests.DiscardBatchAsync_MarksActionsDiscarded_WritesNoDomainRows` — existing controls |
| 8 | ✅ | Live: discarding a staged review batch through the REST endpoint resolves its alert | Live (T2) | `docs/automated-testing/import-and-staged-actions/20-pending-review-alert.md` step 5 |
| 9 | ✅ | Live: the discard document passes against the current planner | Live (T2) | `docs/automated-testing/import-and-staged-actions/04-discard.md` |
