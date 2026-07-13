# #176 — Conversation: Description-field Modify/decidability

**Status:** Waiting for release
**GitHub issue:** #176
**Tiers required:** T1, T2
**Depends on:** #162, #165, #168 (shipped patterns this builds on); not technically blocked by #170-#175 but sequenced last among the entity-Modify issues

---

## Spec requirements (from the GitHub issue)

1. `Sql.Conversations` (`src/Quotinator.Engine/Queries/Sql.cs`) gains `SelectExistingById` (returns
   `Description`, `CompletenessStatus` — distinct from today's bare `SelectIdById`, which is only an
   existence check), `UpdateDescriptionById`, `SelectCompletenessById`, `UpdateCompletenessById`.
2. `PlanConversationsAsync` (`src/Quotinator.Engine/Database/ImportActionPlanner.cs`) gains an
   id-match branch before today's "exists → skip" check: id found → diff `description` only (never
   touches `lines`) → `CompletenessGuard.ShouldBlock` evaluated against the policy-**resolved** value
   → stage `Blocked` or `Modify`. Id not found → today's `Add` path (full line list staged) is
   unchanged.
3. `ApplyResolvedActionAsync`'s Conversation case gains a `Modify` branch — `UPDATE Conversations SET
   Description=..., DateModified=...` via the new `UpdateDescriptionById`. Lines are never touched by
   a Modify apply (this issue's scope boundary).
4. `DecideAsync` gains an `EntityType == Conversation && ActionType == Modify` branch — the only
   mergeable field is `description`, so this is the simplest of the batch (no list-typed field like
   Quote's `genres`).
5. `ComputeAmbiguousFields` gains a `Conversation` case (in practice `description` is the only field
   that can ever be ambiguous — this mostly proves out the plumbing).
6. `ReverseAppliedActionsAsync`'s Conversation case splits on `ActionType`: `Add` keeps today's
   unconditional soft-delete (no active-reference check needed — nothing FKs to a Conversation);
   `Modify` restores `description` via the new `UpdateDescriptionById` from `ExistingValue`.
7. `ConflictDecisionRequest` gains `ConversationDescription` (nullable `FieldDecision?`).
8. No change needed to `ClearStaleAddTargetsAsync` — a Modify action is never in its `adds` filter,
   and Conversation's existing raw-SQL cleanup path is already correct (its id has been
   explicit-in-file, like Quote's, since #67/#68).

---

## Implementation notes from reading the current code (2026-07-12)

- **#171/#172/#173/#175 are not yet implemented in this codebase** — their plan docs exist
  (`171-stagedirection-modify-plan.md` etc.) but all four are still `Status: Planning`, and
  `PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/`PlanConversationsAsync` (`ImportActionPlanner.cs:403-478`)
  are today all bare "exists → skip, else Add" — no Modify branch exists anywhere in the StageDirection/
  SoundCue/Conversation family yet. The concrete, shipped precedent to copy is **Source's** Modify
  pattern (#162, `PlanSourcesAsync`, `ImportActionPlanner.cs:292-396`), not StageDirection's — #171's
  own plan doc explicitly designs itself to mirror `PlanSourcesAsync`, so copying `PlanSourcesAsync`
  directly for Conversation reaches the same target shape #171 would have produced, without depending
  on #171 having landed first.
- **Conversation's Modify branch is structurally simpler than Source's, in the same way #171 notes for
  StageDirection.** Source needed two lookup paths (`SelectExistingById` first, natural-key fallback
  second) because pre-#162 Source rows only had a natural-key identity. Conversation has always been
  purely id-keyed since #67/#68 (`Sql.Conversations.SelectIdById` is the only existence check today) —
  there is no second branch to add. "Mirror `PlanSourcesAsync`'s shape" means mirror the **id-match
  branch's internal logic** (diff → policy resolution → `ShouldBlock` on the resolved value → stage
  `Modify`/`Blocked`), not the dual-lookup structure. The existing `SelectIdById` → not-found → `Add`
  path (which builds the full `ConversationLinePayload` list) stays exactly as it is.
- **Conversation is the simplest entity in the whole Modify batch: exactly one mergeable field.**
  Unlike StageDirection (`text`+`imageUrl`) or Source (`title`+`type`+`date`), there is only
  `description` to diff, resolve, and restore — no multi-field map, no list-typed field like Quote's
  `genres` (the #168 `List<string>`-reference-equality bug class does not apply here at all, since
  `FieldMergeResolver.ValuesEqual` is only needed for genuinely non-trivial value types).
- **`ToFieldMap(ConversationActionPayload)` already exists** (`SqliteImportActionService.cs:839-840`):
  `new Dictionary<string, object?> { ["description"] = payload.Description, ["lineCount"] =
  payload.Lines.Count }` — added earlier purely to support `BuildFields`'s existing/incoming display in
  `GET /import/actions` responses, not for any diff/merge logic. **`lineCount` must never be treated as
  a mergeable field** — it is a derived display value, not something `FieldMergeResolver` should ever
  resolve or decide on. The new diff/resolve logic this issue adds must build its own `description`-only
  field map for `ShouldBlock`/`ResolveWithDecisions`, not reuse this existing helper's `lineCount` entry.
- **Confirmed no `Sql.Conversations.CountActiveReferences` exists** (`Sql.cs:274-280`'s own remark
  explains why: nothing carries an FK to a Conversation, `ConversationLines` point away from it, same
  as `QuoteGenres`/`QuoteTranslations` point away from Quote) — the Modify reversal branch needs no
  active-reference check, mirroring Source's Modify reversal (`SqliteImportActionService.cs:331-347`),
  which also skips that check for the same "restore, never delete" reason.
- **`ComputeAmbiguousFields`'s `default: return [];`** (`SqliteImportActionService.cs:866-867`) is what
  Conversation currently falls through to — confirmed no case exists for it yet.
- **`DecideAsync`'s current gating** (`SqliteImportActionService.cs:90-111`) special-cases Source first
  (`if (action.EntityType == Source && ActionType == Modify) { ...; return; }`), then rejects anything
  that isn't `Quote`. The new Conversation branch is added the same way — before the `!= Quote`
  rejection, alongside the Source check, not replacing it.
- **`ReverseAppliedActionsAsync`'s Source `Modify` branch** (`SqliteImportActionService.cs:331-347`) is
  the exact shape to copy: deserialize `ExistingValue`, `UpdateFieldsById`-equivalent with it, log the
  change, then `break` before reaching the reference-count/soft-delete code below it that only applies
  to `Add`. Conversation's own reversal switch entry (`SqliteImportActionService.cs:363-372`) currently
  has only the unconditional `Add`-shaped soft-delete; it needs the same `ActionType` split Source
  already has.
- **`PlanConversationsAsync` currently has no `DuplicateResolutionPolicy` parameter** (unlike
  `PlanSourcesAsync`) — it needs one added, and the call site at `ImportActionPlanner.cs:167`
  (`await PlanConversationsAsync(connection, conversations ?? [], batchIdStr, actions, now,
  transaction);`) updated to pass the `policy` local already in scope in `PlanAsync` (same variable
  `PlanSourcesAsync`'s call at line 58 already uses).
- **`ConversationActionPayload` is `internal sealed record ConversationActionPayload(string?
  Description, IReadOnlyList<ConversationLinePayload> Lines)`** (`ImportActionPlanner.cs:525`). A
  Modify action's `ExistingValue`/`IncomingValue`/`MergedFields` payloads only need `Description` to be
  meaningful for this issue's scope — construct them with an empty `Lines` list (`[]`), matching how
  #171 plans to leave `StageDirectionActionPayload.Translations` empty on a Modify payload for the same
  reason (a field this issue's Modify never reads or writes still needs *some* value to satisfy the
  record's constructor).

---

## Steps

### 1. Write the red tests

**Status:** ✅ Done. 7 tests added; confirmed red before implementation (per red-before-green policy).

Add the seven tests from the issue's "Expected tests" table: the five `PlanConversationsAsync_*` tests
to `Quotinator.Engine.Tests/Database/ImportActionPlannerTests.cs`, and `DecideAsync_ConversationModify_ResolvesDescriptionDecision`
/ `ReverseBatchAsync_ConversationModify_RestoresDescriptionOnly` to
`Quotinator.Engine.Tests/Services/SqliteImportActionServiceTests.cs` — matching where #162's/#168's
equivalent Source tests live. Confirm all seven fail against current (pre-implementation) code, per
this project's red-before-green policy.

### 2. Add `Sql.Conversations` query additions

**Status:** ✅ Done — matches the plan exactly.

Add `SelectExistingById` (`SELECT Description, CompletenessStatus FROM Conversations WHERE Id = @id
AND IsDeleted = 0;`), `UpdateDescriptionById` (`UPDATE Conversations SET Description = @description,
DateModified = @dateModified WHERE Id = @id;`), `SelectCompletenessById` (`SELECT CompletenessStatus,
NoValueKnown FROM Conversations WHERE Id = @id;`), `UpdateCompletenessById` (`UPDATE Conversations SET
CompletenessStatus = @completenessStatus, DateModified = @dateModified WHERE Id = @id;`) — same shapes
as `Sql.Sources`'/the not-yet-landed `Sql.StageDirections`' equivalents (`Sql.cs:244-257`).

### 3. Add the id-match Modify branch to `PlanConversationsAsync`

**Status:** ✅ Done — mirrors `PlanSourcesAsync`'s id-match branch; `lines` never diffed.

Add a `DuplicateResolutionPolicy policy` parameter; update the `PlanAsync` call site
(`ImportActionPlanner.cs:167`) to pass it. Inside the existing `foreach`, replace the current
`SelectIdById` existence-only check with `SelectExistingById`: if a row is found, diff `description`
only — existing/incoming field maps of exactly one key (`{ ["description"] = ... }`), raw diff for the
unchanged-check (using `FieldMergeResolver.ValuesEqual`, silent-reuse `continue` if unchanged, per
#168's lesson even though `description` is a plain string with no reference-equality risk — stay
consistent with every other entity's diff call site) → policy-resolved value (`skip` ⇒ existing value;
`merge-ours`/`merge-theirs` ⇒ `FieldMergeResolver.Resolve`; anything else ⇒ incoming value) →
resolved-vs-existing diff → `CompletenessGuard.ShouldBlock` on that resolved diff → stage `Blocked`
(paths mirroring `PlanSourcesAsync:337-351`) or `Modify`/`Pending`/`Decided` per policy (mirroring
`PlanSourcesAsync:353-369`). `Lines` is never read, diffed, or included as a mergeable field anywhere
in this branch — the `ExistingValue`/`IncomingValue`/`MergedFields` payloads carry `Lines = []`. If not
found, keep the existing `Add` path (full `ConversationLinePayload` list built from `c.Lines`)
unchanged.

### 4. Add the `Modify` branch to `ApplyResolvedActionAsync`'s Conversation case

**Status:** ✅ Done.

Split the existing case (`SqliteImportActionService.cs:632-663`) on `action.ActionType.Parsed`. `Add`
keeps today's `InsertIfNotExists` + line-insert loop unchanged. `Modify` deserializes `MergedFields`
into a `ConversationActionPayload`, calls the new `Sql.Conversations.UpdateDescriptionById` with its
`Description`, logs the change via `QuoteSeedWriter.LogChangeAsync`, then calls
`ApplyCompletenessAsync(..., Sql.Conversations.SelectCompletenessById, Sql.Conversations.UpdateCompletenessById,
action.EntityId, action.MarkCompletenessAs.Parsed, now)` — mirrors Source's Modify apply
(`SqliteImportActionService.cs:492-518`, referenced by #171 as its own template too). `Lines` is never
touched by the Modify branch — no `ConversationLines` read, insert, or delete of any kind.

### 5. Add Conversation's `DecideAsync` branch

**Status:** ✅ Done.

Add an `EntityType == Conversation && ActionType == Modify` branch before the existing `!= Quote`
rejection (`SqliteImportActionService.cs:110`), alongside the Source branch already there
(`SqliteImportActionService.cs:90-108`). New `ToConversationDecisionMap(ConflictDecisionRequest
request)` helper mapping `description` from `request.ConversationDescription` — mirrors
`ToSourceDecisionMap` (`SqliteImportActionService.cs:929-944`) but with a single `Add("description",
request.ConversationDescription);` call, the simplest decision map in the whole batch. Deserialize
existing/incoming `ConversationActionPayload`, build single-key field maps, call
`FieldMergeResolver.ResolveWithDecisions`, re-serialize a resolved `ConversationActionPayload` with
`Lines = []` (never `incomingPayload.Lines` or `existingPayload.Lines` — see step 3's scope note), pass
to `_coordinator.DecideAsync`.

### 6. Add Conversation to `ComputeAmbiguousFields`

**Status:** ✅ Done — confirmed live via T2: `"ambiguousFields":["description"]` reported correctly.

Add a `case ImportActionEntityTypes.Conversation:` alongside the existing `Source` case
(`SqliteImportActionService.cs:860-865`), deserializing `ConversationActionPayload` and building a
single-key `description` field map directly (not the existing `ToFieldMap(ConversationActionPayload)`
helper at line 839-840, which also emits `lineCount` — a derived display field that must never be
offered to `FieldMergeResolver.ResolveWithDecisions` as something requiring a decision).

### 7. Split `ReverseAppliedActionsAsync`'s Conversation case on `ActionType`

**Status:** ✅ Done — confirmed live via T2 (single-shot apply/reverse cycle restored description, lines untouched).

`Modify`: deserialize `ExistingValue`, call `Sql.Conversations.UpdateDescriptionById` to restore
`Description`, log the change, `break` before the soft-delete code. `Add`: keep today's unconditional
soft-delete unchanged (`SqliteImportActionService.cs:363-372`) — no active-reference check needed
either way, since nothing FKs to a Conversation (confirmed via the absence of
`Sql.Conversations.CountActiveReferences` — see implementation notes above) — mirrors Source's split
(`SqliteImportActionService.cs:331-347`).

### 8. Add `ConflictDecisionRequest.ConversationDescription`

**Status:** ✅ Done.

Add `ConversationDescription` (nullable `FieldDecision?`), placed after the existing
`SourceTitle`/`SourceType`/`SourceDate` properties (`ConflictDecisionRequest.cs:37-44`), with a doc
comment following the existing `/// <summary>Decision for a Source action's ... (#162).</summary>`
pattern but referencing `#176`.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | An id-matched Conversation with a `description` diff stages `Modify`, not silently reused | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanConversationsAsync_IdMatchFound_DescriptionDiffers_StagesModifyAction` |
| 2 | ✅ | An id-matched Conversation with nothing changed stages nothing (silent reuse) | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanConversationsAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 3 | ✅ | An id-matched Conversation's `lines` are never read, diffed, or included in the staged Modify payload | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanConversationsAsync_IdMatchFound_LinesNeverDiffed` |
| 4 | ✅ | A `Complete`-status id-matched Conversation with a policy-resolved `description` change stages `Blocked`, not `Modify` | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanConversationsAsync_CompleteStatus_StagesBlockedNotModify` |
| 5 | ✅ | A `Complete`-status Conversation under `Skip` policy never blocks (resolved value is always the existing value) | Unit test | `Quotinator.Engine.Tests.ImportActionPlannerTests.PlanConversationsAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 6 | ✅ | Decide endpoint accepts a Conversation `description` field decision | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.DecideAsync_ConversationModify_ResolvesDescriptionDecision` |
| 7 | ✅ | Reversing a Conversation Modify restores `ExistingValue`'s `description` only, never touches `lines` | Unit test | `Quotinator.Engine.Tests.SqliteImportActionServiceTests.ReverseBatchAsync_ConversationModify_RestoresDescriptionOnly` |
| 8 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 1,194/1,194 passing, 0 warnings, 0 errors |
| 9 | ✅ | Live: a `Complete` Conversation's `description` cannot be silently overwritten via re-import; a correctable one can be Modified/decided/reversed end to end through `POST /api/v1/import`, with `lines` never affected | Live (T2) | Docker smoke test: imported a `conversations[]` entry, decided a Pending Modify with `markCompletenessAs: Complete` (`ambiguousFields` correctly reported `["description"]`), re-imported a changed `description` under `review` — confirmed the resulting action was `Blocked` and `GET /api/v1/conversations/{id}` showed the on-disk description and lines unchanged. Separately, single-shot corrected a non-`Complete` Conversation's `description`, confirmed the write via `GET /api/v1/conversations/{id}`, then reversed it and confirmed the pre-correction description was restored with lines still intact throughout. Used the single-shot path for reverse due to the pre-existing #177 `ImportBatches.Status` gap. |
| 10 | ✅ | App still opens and builds in Visual Studio | Live (T1) | Developer confirmed clean startup in Visual Studio, multiple `GET /api/v1/quotes/random` → 200, no errors |

---

## Notes

T1 and T2 are both required — per this project's blanket rule (no exemption for a change with no
Razor/migration surface; see #168's own Notes section and the process-gap discussion it references).

**`lines` are never diffed, read, or written by anything this issue adds.** A Conversation's line
editing (add/remove/reorder/retype) remains entirely out of scope — `FieldMergeResolver` has no
vocabulary for an ordered, discriminated, referential collection like `ConversationLines`, and
`CompletenessGuard.ShouldBlock`'s semantics for a line edit (is reordering two lines a "change" that
should hold a `Complete` conversation? is appending a line without touching existing ones?) are
genuinely unresolved design questions, not implementation details this issue happens to skip. Line
editing is deferred to a separate future issue not yet filed. This issue gives Conversation full
decide/apply/reverse/audit-trail parity with every other entity for its one scalar field only.

This issue's seven new tests must be confirmed red before implementation, per this project's
red-before-green policy (`feedback_red_green_required` in memory) — applies here the same way #168's
step 1 and #171's step 1 confirm their new tests red before the fix lands.
