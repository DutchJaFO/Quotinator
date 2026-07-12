# #171 — StageDirection: Modify/decidability

**Status:** Planning
**GitHub issue:** #171
**Tiers required:** T1, T2
**Depends on:** #162, #165, #168 (all shipped — this issue builds on their pattern, not literally blocked by them being merged to main yet)

---

## Spec requirements (from the GitHub issue)

1. `Sql.StageDirections` (`src/Quotinator.Engine/Queries/Sql.cs`) gains `SelectExistingById` (returns
   `Text`, `ImageUrl`, `CompletenessStatus`), `UpdateFieldsById`, `SelectCompletenessById`,
   `UpdateCompletenessById` — the last two are needed by the existing generic `ApplyCompletenessAsync`
   helper and are easy to miss since only Quote/Source have them today.
2. `PlanStageDirectionsAsync` (`src/Quotinator.Engine/Database/ImportActionPlanner.cs`) rewritten to
   mirror `PlanSourcesAsync`'s shape exactly: id-match lookup → compute existing/incoming field maps
   (`text`, `imageUrl`) → unchanged-check (silent reuse, same as today) → policy-based resolution
   (skip/merge/newest-wins) → `CompletenessGuard.ShouldBlock` evaluated against the policy-**resolved**
   value → stage `Blocked` or `Modify`. The existing "no id match → Add" path is unchanged.
3. `ApplyResolvedActionAsync`'s StageDirection case (`src/Quotinator.Engine/Services/SqliteImportActionService.cs`)
   splits on `ActionType`: `Add` unchanged; `Modify` calls the new `Sql.StageDirections.UpdateFieldsById`
   against `MergedFields`, and applies the resolved/auto-computed completeness status via the existing
   `ApplyCompletenessAsync` helper.
4. `DecideAsync` gains an `EntityType == StageDirection && ActionType == Modify` branch, mirroring
   Source's branch shape exactly (deserialize existing/incoming payloads → field maps →
   `FieldMergeResolver.ResolveWithDecisions` → re-serialize → `_coordinator.DecideAsync`).
5. `ComputeAmbiguousFields` gains a `StageDirection` case.
6. `ReverseAppliedActionsAsync`'s StageDirection case splits on `ActionType`: `Add` keeps today's
   soft-delete-if-unreferenced behaviour; `Modify` restores `Text`/`ImageUrl` via `UpdateFieldsById`
   from `ExistingValue`.
7. `ConflictDecisionRequest` gains `StageDirectionText`, `StageDirectionImageUrl` (nullable
   `FieldDecision?`).
8. Translations (`StageDirectionTranslations`) are explicitly **not** part of the mergeable field set
   in this issue — a translation-dict diff is a distinct, out-of-scope nested-merge problem. A Modify
   apply leaves existing translation rows untouched; only `text`/`imageUrl` on the parent row become
   correctable.

---

## Implementation notes from reading the current code (2026-07-12)

- **StageDirection's Modify branch is structurally simpler than Source's.** Source needed two lookup
  paths (`SelectExistingById` first, `SelectExistingByTitleAndType` fallback) because pre-#162 Source
  rows only ever had a natural-key identity. StageDirection has always been purely id-keyed since
  #67/#68 (`Sql.StageDirections.SelectIdById` is the only existence check today, and
  `PlanStageDirectionsAsync` has no natural-key concept anywhere) — there is no equivalent second
  branch to add. "Mirror `PlanSourcesAsync`'s shape" (item 2) means mirror the **id-match branch's
  internal logic** (diff → policy resolution → `ShouldBlock` on the resolved value → stage
  `Modify`/`Blocked`), not the dual-lookup structure. The existing `SelectIdById` → not-found → `Add`
  path stays exactly as it is.
- **`PlanStageDirectionsAsync` currently has no `DuplicateResolutionPolicy` parameter** (unlike
  `PlanSourcesAsync`) — it needs one added, and the call site at `ImportActionPlanner.cs:165`
  (`await PlanStageDirectionsAsync(connection, stageDirections ?? [], batchIdStr, actions, now,
  transaction);`) updated to pass the `policy` local already in scope in `PlanAsync` (same variable
  `PlanSourcesAsync`'s call at line 58 already uses). No `sourceIndex`-equivalent cache is needed —
  nothing resolves a StageDirection by anything other than its own explicit id (a Conversation's lines
  reference it directly by id, never by lookup).
- **#168's lesson applies from the start here:** `ShouldBlock`'s changed-field set must be computed
  from the policy-**resolved** `text`/`imageUrl` values (post skip/merge/newest-wins), not the raw
  incoming ones — exactly the ordering `PlanSourcesAsync` was fixed to use in #168. Use
  `FieldMergeResolver.ValuesEqual` (already `public`, `Quotinator.Data.Import`) for every diff, not a
  naive `Equals` — `Skip` policy's resolved value is always the existing value, so it must never block
  a `Complete` StageDirection.
- **`StageDirectionActionPayload`'s `Translations` property is non-nullable** (`IReadOnlyDictionary<string,
  SourceStageDirectionTranslation>`, required positional parameter). Since translations are explicitly
  out of scope for Modify (item 8), a Modify action's `ExistingValue`/`IncomingValue` payloads only need
  `Text`/`ImageUrl` to be meaningful — construct them with an empty `Translations` dictionary (`new
  Dictionary<string, SourceStageDirectionTranslation>()`), matching that `ToFieldMap(StageDirectionActionPayload)`
  already only reads `Text`/`ImageUrl` and ignores `Translations` regardless. `Sql.StageDirections.
  SelectExistingById` therefore only needs to select `Text`, `ImageUrl`, `CompletenessStatus` — exactly
  as item 1 specifies — not translation rows.
- **`DecideAsync`'s current gating** (`SqliteImportActionService.cs:86-111`) special-cases Source first
  (`if (action.EntityType == Source && ActionType == Modify) { ...; return; }`), then rejects anything
  that isn't `Quote`. The new StageDirection branch is added the same way — before the `!= Quote`
  rejection, alongside the Source check, not replacing it.
- **`ReverseAppliedActionsAsync`'s Source `Modify` branch** (`SqliteImportActionService.cs:331-347`) is
  the exact shape to copy: deserialize `ExistingValue`, `UpdateFieldsById` with it, log the change, then
  `break` before reaching the reference-count/soft-delete code below it that only applies to `Add`.

---

## Steps

### 1. Write the red tests

**Status:** Not started.

Add the six tests from the issue's "Expected tests" table to `Quotinator.Engine.Tests`
(`ImportActionPlannerTests.cs` for the four planning tests, `SqliteImportActionServiceTests.cs` for the
decide/reverse tests — matching where #162's/#168's equivalent Source tests live). Confirm all six fail
against current (pre-implementation) code — either by running them before any production code changes,
or via `git stash` once the implementation exists, per this project's red-before-green policy.

### 2. Add `Sql.StageDirections` query set

**Status:** Not started.

Add `SelectExistingById` (`SELECT Text, ImageUrl, CompletenessStatus FROM StageDirections WHERE Id =
@id AND IsDeleted = 0;`), `UpdateFieldsById` (updates `Text`, `ImageUrl`, `DateModified`), `SelectCompletenessById`
(`SELECT CompletenessStatus, NoValueKnown FROM StageDirections WHERE Id = @id;`), `UpdateCompletenessById`
(`UPDATE StageDirections SET CompletenessStatus = @completenessStatus, DateModified = @dateModified
WHERE Id = @id;`) — same shapes as `Sql.Sources`' equivalents (`Sql.cs:244-257`).

### 3. Rewrite `PlanStageDirectionsAsync`

**Status:** Not started.

Add a `DuplicateResolutionPolicy policy` parameter; update the `PlanAsync` call site (`ImportActionPlanner.cs:165`)
to pass it. Inside the existing `foreach`, after the current `SelectIdById` lookup: if found, branch
into the new Modify logic (existing/incoming field maps for `text`/`imageUrl` → raw diff for the
unchanged-check, using `FieldMergeResolver.ValuesEqual` → policy-resolved value → resolved-vs-existing
diff → `CompletenessGuard.ShouldBlock` → stage `Blocked` or `Modify`/`Pending`/`Decided` per policy,
mirroring `PlanSourcesAsync`'s id-match branch structure (`ImportActionPlanner.cs:303-370`) field for
field). If not found, keep the existing `Add` path unchanged.

### 4. Split `ApplyResolvedActionAsync`'s StageDirection case on `ActionType`

**Status:** Not started.

`Add` keeps calling `EnsureStageDirectionExistsAsync` as today. `Modify` deserializes `MergedFields`,
calls `Sql.StageDirections.UpdateFieldsById`, logs the change via `QuoteSeedWriter.LogChangeAsync`, then
calls `ApplyCompletenessAsync(..., Sql.StageDirections.SelectCompletenessById, Sql.StageDirections.UpdateCompletenessById,
action.EntityId, action.MarkCompletenessAs.Parsed, now)` — mirrors Source's case
(`SqliteImportActionService.cs:492-518`) exactly.

### 5. Add StageDirection's `DecideAsync` branch

**Status:** Not started.

Add an `EntityType == StageDirection && ActionType == Modify` branch before the existing `!= Quote`
rejection (`SqliteImportActionService.cs:110`), alongside the Source branch already there. New
`ToStageDirectionDecisionMap(ConflictDecisionRequest request)` helper mapping `text`/`imageUrl` from
`request.StageDirectionText`/`request.StageDirectionImageUrl`, mirroring `ToSourceDecisionMap`
(`SqliteImportActionService.cs:929-944`).

### 6. Add StageDirection to `ComputeAmbiguousFields`

**Status:** Not started.

Add a `case ImportActionEntityTypes.StageDirection:` alongside the existing `Source` case
(`SqliteImportActionService.cs:860-865`), deserializing `StageDirectionActionPayload` and building field
maps via the existing `ToFieldMap(StageDirectionActionPayload)` helper (already present, `text`/`imageUrl`
only).

### 7. Split `ReverseAppliedActionsAsync`'s StageDirection case on `ActionType`

**Status:** Not started.

`Modify`: deserialize `ExistingValue`, call `Sql.StageDirections.UpdateFieldsById` to restore `Text`/
`ImageUrl`, log the change, `break` before the reference-count check. `Add`: keep today's
soft-delete-if-unreferenced behaviour unchanged (`SqliteImportActionService.cs:373-378`) — mirrors
Source's split (`SqliteImportActionService.cs:331-347`).

### 8. Add `ConflictDecisionRequest` properties

**Status:** Not started.

Add `StageDirectionText`, `StageDirectionImageUrl` (both nullable `FieldDecision?`), placed after the
existing `SourceTitle`/`SourceType`/`SourceDate` properties (`ConflictDecisionRequest.cs:37-44`), each
with a doc comment following the existing `/// <summary>Decision for a Source action's ... (#162).</summary>`
pattern but referencing `#171`.

### 9. Confirm translations are untouched

**Status:** Not started.

No production change needed beyond what step 3's payload construction already does (empty
`Translations` dict on Modify's `ExistingValue`/`IncomingValue`/`MergedFields`) — verified by asserting,
in the new `ReverseBatchAsync_StageDirectionModify_RestoresExistingValue` test, that a pre-existing
`StageDirectionTranslations` row survives a Modify + reversal cycle untouched.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | An id-matched StageDirection with a `text` diff stages `Modify`, not silently reused | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanStageDirectionsAsync_IdMatchFound_TextDiffers_StagesModifyAction` |
| 2 | ❌ | An id-matched StageDirection with nothing changed stages nothing (silent reuse) | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanStageDirectionsAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 3 | ❌ | A `Complete`-status id-matched StageDirection with a policy-resolved field change stages `Blocked`, not `Modify` | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanStageDirectionsAsync_CompleteStatus_StagesBlockedNotModify` |
| 4 | ❌ | A `Complete`-status StageDirection under `Skip` policy never blocks (resolved value is always the existing value) | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanStageDirectionsAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 5 | ❌ | Decide endpoint accepts StageDirection `text`/`imageUrl` field decisions | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.DecideAsync_StageDirectionModify_ResolvesFieldDecisions` |
| 6 | ❌ | Reversing a StageDirection Modify restores `ExistingValue`'s fields, leaves translation rows untouched | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.ReverseBatchAsync_StageDirectionModify_RestoresExistingValue` |
| 7 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 8 | ❌ | Live: a `Complete` StageDirection's field cannot be silently overwritten via re-import; a correctable one can be Modified/decided/reversed end to end through `POST /api/v1/import` | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .`, matching #168's row 6 style: import a file with an explicit `stageDirections[]` entry, decide it `Complete` via `markCompletenessAs`, re-import a changed `text`/`imageUrl` for the same id under `Review` policy — confirm the resulting action is `Blocked` (not `Pending`) and the on-disk `Text`/`ImageUrl` are unchanged; separately, stage/decide/apply a `text` correction on a non-`Complete` StageDirection, confirm the write lands, then `POST /import/actions/reverse?batchId=...` and confirm the pre-correction `text`/`imageUrl` are restored and any existing translation row for that StageDirection is untouched throughout |

---

## Notes

T1 and T2 are both required — per this project's blanket rule (no exemption for a change with no
Razor/migration surface; see #168's own Notes section and the process-gap discussion it references).

This issue's six new tests must be confirmed red before implementation, per this project's red-before-green
policy (`feedback_red_green_required` in memory) — applies here the same way #168's step 1 confirmed its
two new tests red via `git stash` before the fix landed.
