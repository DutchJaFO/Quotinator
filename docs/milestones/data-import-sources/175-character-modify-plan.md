# #175 — Character: explicit id, Modify/decidability

**Status:** Planning
**GitHub issue:** #175
**Tiers required:** T1, T2
**Depends on:** #174 (must land first — see Notes)

---

## Spec requirements (from the GitHub issue)

1. `schemas/source-extended.schema.json` gains a `characters` array + `character` `$def`: `id`
   (required, UUID v4 pattern), `name` (required). No `sourceTitle`/`sourceType` — #174 makes
   Character source-independent, so a character entry no longer links to any specific Source.
2. New `CharacterEntry.cs` record in `Quotinator.Core.Import`, doc-commented like
   `PersonEntry`/`SourceEntry`. `ParsedSourceFile` gains `Characters` (defaults `[]`).
   `SourceQuoteFileReader.TryParseExtended` gains the new root-key parse.
3. `Sql.Characters` (`src/Quotinator.Engine/Queries/Sql.cs`) gains `SelectExistingById` (returns
   `Name`, `CompletenessStatus`), `UpdateFieldsById`, `SelectCompletenessById`,
   `UpdateCompletenessById`.
4. New `PlanCharactersAsync` (`src/Quotinator.Engine/Database/ImportActionPlanner.cs`), mirroring
   #173's `PlanPeopleAsync` shape exactly: id-match lookup → field-map diff (`name` only) →
   unchanged-check → policy-based resolution → `CompletenessGuard.ShouldBlock` evaluated against
   the policy-**resolved** value → stage `Blocked` or `Modify`. Falls back to
   `Sql.Characters.SelectIdByName` (added by #174) when no id match. A character discovered only
   implicitly through a Quote's `character` string (no explicit `characters[]` entry) stays
   Add-only forever, same rule as Person.
5. `ApplyResolvedActionAsync`'s Character case splits on `ActionType`: `Add` unchanged; `Modify`
   calls the new `Sql.Characters.UpdateFieldsById`.
6. `DecideAsync` gains an `EntityType == Character && ActionType == Modify` branch.
7. `ComputeAmbiguousFields` gains/updates the `Character` case for the new global shape.
8. `ReverseAppliedActionsAsync`'s Character case splits on `ActionType`: `Add` keeps
   soft-delete-if-unreferenced; `Modify` restores `Name` via `UpdateFieldsById` from
   `ExistingValue`.
9. `ClearStaleAddTargetsAsync`'s Character cleanup branch switches from the Guid-typed repository
   path to the raw-SQL, case-preserving pattern — same fix #162 made for Source and #173 will make
   for Person, needed because an explicit `characters[]` id is file-authored and not guaranteed
   uppercase.
10. `ConflictDecisionRequest` gains `CharacterName` (nullable `FieldDecision?`).

---

## Steps

### 1. Schema: `characters` array + `character` `$def`

**Status:** Not started.

Add a top-level `characters` array to `schemas/source-extended.schema.json` (same shape/precedent
as `sources`/`stageDirections`/`soundCues`/`conversations`) referencing a new `character` `$def`
under `$defs`. Fields: `id` (required, UUID-v4 pattern, same regex as the other explicit-id
`$def`s) and `name` (required). Deliberately **no** `sourceTitle`/`sourceType`/any Source linkage —
once #174 lands, Character is source-independent, mirroring the (not-yet-added, #173-pending)
`person` `$def`'s shape rather than the current `source` `$def`'s shape. Purely additive — a file
without a `characters` section parses identically to today.

### 2. `CharacterEntry.cs` DTO and reader wiring

**Status:** Not started.

New `src/Quotinator.Core/Import/CharacterEntry.cs` record, doc-commented like
`SourceEntry`/`PersonEntry` ("assigned at authoring time and never changes"). Two properties only:
`Id` (`required string`) and `Name` (`required string`) — no `Type`/`Date`-equivalent fields exist
for Character. `ParsedSourceFile` gains a `Characters` property (defaults `[]`).
`SourceQuoteFileReader.TryParseExtended` gains the new root-key parse, matching the existing
four/five-section pattern (`sources`, `stageDirections`, `soundCues`, `conversations`, and whatever
`people` section #173 adds).

### 3. `Sql.Characters` new queries

**Status:** Not started.

Add to the `Sql.Characters` nested class in `src/Quotinator.Engine/Queries/Sql.cs`:
- `SelectExistingById` — `SELECT Name, CompletenessStatus FROM Characters WHERE Id = @id AND IsDeleted = 0;`
  (mirrors `Sql.Sources.SelectExistingById`'s shape, minus `Title`/`Type`/`Date`).
- `UpdateFieldsById` — `UPDATE Characters SET Name = @name, DateModified = @dateModified WHERE Id = @id;`
  (mirrors `Sql.Sources.UpdateFieldsById`; never touches `CompletenessStatus`/`NoValueKnown` — see
  its own remark on `UpdateCompletenessById` for why that's separate).
- `SelectCompletenessById` / `UpdateCompletenessById` — same shape as `Sql.Sources`'s and
  `Sql.Quotes`'s own pair, used by `ApplyCompletenessAsync`'s existing before/after read-and-write.

This step assumes `#174` has already added `Sql.Characters.SelectIdByName` (replacing today's
`SelectIdBySourceAndName`) as part of its own natural-key fallback — this issue only adds the four
id-keyed queries above, not the natural-key one.

### 4. `PlanCharactersAsync` in `ImportActionPlanner.cs`

**Status:** Not started.

New private method mirroring `PlanSourcesAsync`'s control flow (id-match → field diff → unchanged
early-continue → policy-resolved value → `CompletenessGuard.ShouldBlock` against the *resolved*
diff, per #168's rule → `Blocked`/`Modify`/`Pending` per policy), but with a single-field
(`name`-only) field map instead of Source's three-field one — the same simplification #173's
`PlanPeopleAsync` is expected to make relative to `PlanSourcesAsync`. Falls back to
`Sql.Characters.SelectIdByName` (added by #174) when no id-match, same natural-key-fallback
contract `PlanSourcesAsync` already has for `Sql.Sources.SelectIdByTitleAndType`. Called from
`PlanAsync` alongside the existing `PlanSourcesAsync`/`PlanStageDirectionsAsync`/etc. calls, indexing
into the same `characterIndex` dictionary `ResolveCharacterAsync` already populates/consults, so a
same-batch quote referencing an explicitly-declared character resolves to the corrected id (same
gap `PlanSourcesAsync_QuoteReferencesExplicitlyDeclaredSource_ResolvesToItsId` caught for Source in
#162).

A character discovered only implicitly through `SourceQuote.Character` (no matching
`characters[]` entry) is never touched by this method and stays Add-only via the existing
`ResolveCharacterAsync`, exactly like a Person discovered only through `SourceQuote.Author`.

### 5. `ApplyResolvedActionAsync`'s Character case — Add/Modify split

**Status:** Not started.

`SqliteImportActionService.ApplyResolvedActionAsync`'s `case ImportActionEntityTypes.Character`
block (currently unconditional `EnsureCharacterExistsAsync`) branches on `action.ActionType`: `Add`
keeps today's behaviour unchanged; `Modify` deserializes `action.MergedFields` and calls the new
`Sql.Characters.UpdateFieldsById`, then applies `ApplyCompletenessAsync` the same way Source's
Modify branch does. Once #174 lands, `EnsureCharacterExistsAsync`'s own signature and the
defensive `EnsureSourceExistsAsync` call currently preceding it (payload `SourceId`/`SourceTitle`/
`SourceType`) are expected to already be gone/changed by #174's own audit of that call site (spec
item 5 of #174) — this step assumes that shape is already in place and only adds the `Modify`
branch on top of it.

### 6. `DecideAsync`'s Character Modify branch

**Status:** Not started.

Add an `EntityType == Character && ActionType == Modify` branch to
`SqliteImportActionService.DecideAsync`, mirroring the existing Source Modify branch: deserialize
`ExistingValue`/`IncomingValue` as the (post-#174) Character payload type, build single-field
(`name`) field maps, resolve via `FieldMergeResolver.ResolveWithDecisions` using the new
`request.CharacterName` decision, and pass the resolved payload to `_coordinator.DecideAsync`.

### 7. `ComputeAmbiguousFields`'s Character case

**Status:** Not started.

Add a `case ImportActionEntityTypes.Character` arm to `ComputeAmbiguousFields`, alongside the
existing `Quote`/`Source` arms — same single-field map built from the post-#174 Character payload,
fed into the same `FieldMergeResolver.ResolveWithDecisions`/`UnresolvedFieldConflictException`
pattern the existing two arms already use.

### 8. `ReverseAppliedActionsAsync`'s Character case — Add/Modify split

**Status:** Not started.

Split the existing `case ImportActionEntityTypes.Character` block on `action.ActionType`: `Modify`
restores `Name` via `Sql.Characters.UpdateFieldsById` from `ExistingValue`, with no active-reference
check (a Modify reversal never deletes anything) — same shape as the existing Source `Modify` branch
in this same method.

**`Add` is *not* unchanged — it also needs the raw-SQL, case-preserving fix.** #173's own plan doc
originally said the same thing this step used to say ("keep today's soft-delete-if-unreferenced
behaviour unchanged") and was wrong: found live via T2 that
`_personRepository.SoftDeleteAsync(Guid.Parse(action.EntityId), uow)` has the identical
case-sensitivity bug as `ClearStaleAddTargetsAsync` (step 9) — `GuidHandler` force-uppercases the
parameter before binding, so it silently matches zero rows against a lowercase-stored explicit id,
and the reversal becomes a no-op that looks like it worked. Switch this branch to
`sqliteConnection.ExecuteAsync(RepositorySql.SoftDelete("Characters"), new { now, id = action.EntityId }, sqliteTransaction)`
— the same raw-SQL pattern Source's own `Add` reversal already uses — from the start, rather than
finding the gap the hard way via this issue's own T2 pass. See
`173-person-modify-plan.md`'s Notes for the full incident writeup. That doc also notes a unit test
can pass "accidentally" if only one of the two call sites is fixed — a soft-deleted-but-not-really
row still looks id-matchable via a plain `SELECT ... WHERE Id = @id`, so a stale-cleanup test can
silently mask a broken reversal underneath it. Write/verify both this issue's own reversal test and
its `ClearStaleAddTargetsAsync` test together, not independently, so neither can mask the other.

### 9. `ClearStaleAddTargetsAsync` raw-SQL fix and `ConflictDecisionRequest.CharacterName`

**Status:** Not started.

`ClearStaleAddTargetsAsync`'s Character cleanup loop currently reads:

```csharp
foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Character))
    await _characterRepository.HardDeleteAsync(Guid.Parse(action.EntityId));
```

This is the Guid-typed repository path — safe today only because every Character Add id is
currently `EntityIdentity`-derived (always uppercase by construction). An explicit `characters[]`
id is file-authored and not guaranteed uppercase, the same gap #162 found and fixed for Source and
#173 has now actually fixed for Person (`SqliteImportActionService.cs`'s `ClearStaleAddTargetsAsync`
Person branch, committed `8756b37`). Switch to the raw-SQL, case-preserving pattern already used for
Source/Conversation/StageDirection/SoundCue/Person:
`quoteConn.ExecuteAsync(RepositorySql.HardDelete("Characters"), new { id = action.EntityId })`.

**This is one of two call sites with the identical bug, not the only one — see step 8 above.** #173
originally scoped only this one (`ClearStaleAddTargetsAsync`) and found the second
(`ReverseAppliedActionsAsync`'s `Add` branch) live via T2, after the first fix alone had already
made its own `ClearStaleAddTargetsAsync` unit test pass — for the wrong reason, since the reversal
silently no-op'd and left the row not-actually-stale. Apply both fixes together for Character from
the start.

Add `CharacterName` (nullable `FieldDecision?`) to `ConflictDecisionRequest.cs`, alongside the
existing `SourceTitle`/`SourceType`/`SourceDate` properties, and wire it into `ToDecisionMap`'s
Character-specific decision-map builder used by step 6/7 above.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A file without a `characters` section parses identically to today | Unit test | `Quotinator.Core.Tests.SourceQuoteFileReader_CharactersSection_ParsesCorrectly` |
| 2 | ❌ | An id-match with a differing `name` stages a `Modify` action | Unit test | `Quotinator.Engine.Tests.PlanCharactersAsync_IdMatchFound_NameDiffers_StagesModifyAction` |
| 3 | ❌ | An id-match with nothing changed stages no action | Unit test | `Quotinator.Engine.Tests.PlanCharactersAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 4 | ❌ | No id-match falls back to the natural-key (`Name`-only) lookup | Unit test | `Quotinator.Engine.Tests.PlanCharactersAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged` |
| 5 | ❌ | A `Complete`-status id-matched row stages `Blocked`, not `Modify` | Unit test | `Quotinator.Engine.Tests.PlanCharactersAsync_CompleteStatus_StagesBlockedNotModify` |
| 6 | ❌ | A `Complete`-status row under `Skip` policy never blocks (#168 rule) | Unit test | `Quotinator.Engine.Tests.PlanCharactersAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 7 | ❌ | Decide endpoint accepts a Character `Modify` field decision | Unit test | `Quotinator.Engine.Tests.DecideAsync_CharacterModify_ResolvesFieldDecisions` |
| 8 | ❌ | Reversing a Character `Modify` restores `ExistingValue`'s `Name` | Unit test | `Quotinator.Engine.Tests.ReverseBatchAsync_CharacterModify_RestoresExistingValue` |
| 9 | ❌ | A lowercase-authored explicit Character id hard-deletes correctly on stale-Add cleanup | Unit test | `Quotinator.Engine.Tests.ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_HardDeletesCorrectly` |
| 10 | ❌ | A lowercase-authored explicit Character id soft-deletes correctly when its `Add` action is reversed (the second of #173's two-call-site case-sensitivity fix — see step 8) | Unit test | `Quotinator.Engine.Tests.ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_SoftDeletesCorrectly` |
| 11 | ❌ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s); `dotnet test --configuration Release` → all projects passing |
| 12 | ❌ | Live: a `characters[]` correction is staged/decided/applied via `POST /api/v1/import`, and a `Complete` Character's `name` cannot be silently overwritten | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .`, same shape as #162's own T2 row: stage/decide/apply an explicit-`characters[]` `Modify`; separately confirm a `Complete` Character under `Skip` policy is not blocked; additionally, exercise a lowercase-id Add → reverse → re-add cycle live and confirm the row's `IsDeleted` flag actually flips (not just assumed) between steps, matching #173's own T2 canary for this exact bug class |
| 13 | ❌ | App still opens and builds in Visual Studio after the schema/migration surface from #174 this issue builds on | Live (T1) | Developer's own Visual Studio pass — app starts cleanly, database reset/reseed both succeed |

---

## Notes

T1 and T2 are both required — per this project's blanket rule (no exemption for a change with real
C# logic and a data-model surface). **This issue cannot start until #174 lands.** Its exact
technical specifics — the new `Sql.Characters` query shapes beyond what's listed here,
`EntityIdentity.CharacterId`'s post-migration signature, `ResolveCharacterAsync`'s replacement, and
whatever `CharacterActionPayload` ends up carrying once its `SourceId`/`SourceTitle`/`SourceType`
fields are audited — depend on decisions #174 makes as part of its own ADR and migration design,
which are deliberately not finalized yet (#174's own issue body: "The exact merge algorithm is this
issue's own design work — it was deliberately not decided during planning"). This plan doc describes
the intended shape assuming #174 lands as scoped (global, `Name`-keyed, no `SourceId`) — revisit
this plan doc's specifics if #174's actual implementation differs from that assumption.

**Reconciled against #173's actual shipped implementation (2026-07-14), now that it exists.** This
plan doc's original projection matches what #173 actually shipped on every point already checked:
`PlanPeopleAsync`'s id-match/natural-key-fallback/`personIndex`-threading shape (direct template for
`PlanCharactersAsync`, step 4), `ConflictDecisionRequest`'s per-field-decision naming convention
(`PersonName`/`PersonDateOfBirth`/`PersonDateOfDeath` → this issue's own `CharacterName`), the
`ToPersonDecisionMap`/`ToFieldMap` helper pattern, plain-`string?` payload fields (never `SafeValue`,
matching `Source.Date`'s convention), and the `ApplyCompletenessAsync` wiring in the `Modify` apply
branch (step 5). **The one place the projection was wrong is steps 8/9 above** — the case-sensitivity
fix needed at two call sites, not the one originally listed. That gap is now closed in this doc.
