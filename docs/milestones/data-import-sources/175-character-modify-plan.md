# #175 — Character: explicit id, Modify/decidability

**Status:** Waiting for release
**GitHub issue:** #175
**Tiers required:** T1, T2
**Depends on:** #174

---

## Spec requirements (from the GitHub issue)

1. `schemas/source-extended.schema.json` gains a `characters` array + `character` `$def`: `id`
   (optional, UUID v4 pattern), `name` (required), `sourceTitle`/`sourceType` (required — the
   Source this entry is anchored to for ADR 013 matching; ignored/never diffed when `id` is
   present). **Widened from an id+name-only shape during this review (2026-07-24, developer
   decision)** — see Step 1's own Notes for why a bare id+name entry can't run ADR 013's real
   matching algorithm on its own.
2. New `CharacterEntry.cs` record in `Quotinator.Core.Import`, doc-commented like
   `PersonEntry`/`SourceEntry`. `ParsedSourceFile` gains `Characters` (defaults `[]`).
   `SourceQuoteFileReader.TryParseExtended` gains the new root-key parse.
3. `Sql.Characters` (`src/Quotinator.Core/Queries/Sql.cs`) gains `SelectExistingById` (returns
   `Name`, `CompletenessStatus`), `UpdateFieldsById`, `SelectCompletenessById`,
   `UpdateCompletenessById`.
4. New `PlanCharactersAsync` (`src/Quotinator.Core/Database/ImportActionPlanner.cs`), mirroring
   #173's `PlanPeopleAsync` shape for the shared name-diff/policy-resolution/`CompletenessGuard.
   ShouldBlock` flow, but with a Character-specific id/natural-key resolution stage in front of it:
   id-match lookup (when `id` is present) → on no match, resolve `sourceTitle`/`sourceType` to a
   Source (via `sourceIndex`, then `Sql.Sources.SelectIdByTitleAndType`, then a staged Source `Add`)
   and run `Sql.Characters.SelectGlobalCandidateId` (ADR 013's real Type-anchored, Series-scoped
   algorithm) → on no candidate, stage a genuinely-new `Add`, honouring the entry's own explicit id
   as the new row's id when one was supplied (mirroring `PlanSourcesAsync`'s own `canonicalId ??
   EntityIdentity.SourceId(...)` precedent) and falling back to an `EntityIdentity`-derived id only
   when the entry carried none. A character discovered only implicitly through a Quote's `character`
   string (no explicit `characters[]` entry) stays Add-only forever, same rule as Person.
5. `ApplyResolvedActionAsync`'s Character case splits on `ActionType`: `Add` unchanged; `Modify`
   calls the new `Sql.Characters.UpdateFieldsById`.
6. `DecideAsync` gains an `EntityType == Character && ActionType == Modify` branch.
7. `ComputeAmbiguousFields` gains/updates the `Character` case for the new global shape.
8. `ReverseAppliedActionsAsync`'s Character case splits on `ActionType`: `Add` keeps
   soft-delete-if-unreferenced; `Modify` restores `Name` via `UpdateFieldsById` from
   `ExistingValue`.
9. `ClearStaleAddTargetsAsync`'s Character cleanup branch — re-verified live, not assumed: this
   session's earlier #200–205 (`RepositorySql` → `IdClauses`) and #176–178 (`ToCanonicalId()` choke
   point) work already made the Guid-typed repository path case-insensitive end to end, so the
   raw-SQL switch #162/#173 needed for Source/Person is **not** needed here — confirmed by writing
   `ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_HardDeletesCorrectly` and
   `ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_SoftDeletesCorrectly` against the unmodified
   code and both passing outright.
10. `ConflictDecisionRequest` gains `CharacterName` (nullable `FieldDecision?`).

---

## Steps

### 1. Schema: `characters` array + `character` `$def`

**Status:** Done.

**Design decided 2026-07-24 (developer chose "widen the schema" over "drop the fallback" — see
Notes for the full framing of that choice).** Add a top-level `characters` array to
`schemas/source-extended.schema.json` (same shape/precedent as
`sources`/`stageDirections`/`soundCues`/`conversations`) referencing a new `character` `$def` under
`$defs`, mirroring `source`'s own two-shape pattern rather than `person`'s id-only one:

- `id` (optional, UUID-v4 pattern, case-insensitive — same as `source`'s own `id`). Present →
  **Correction** shape: matched by that explicit id, `name` is the only correctable field.
- `name` (required).
- `sourceTitle` (required) / `sourceType` (required, same enum as `source.type`) — the Source this
  entry is anchored to for ADR 013 matching purposes. **Unconditionally required** (matching
  `source`'s own `title`/`type` being unconditionally required regardless of shape), but only
  actually *used* on the Creation/Enrichment path (`id` absent) — ignored on the Correction path,
  since `SourceType` is immutable once a Character exists (ADR 013 Decision 9) and there is nothing
  to diff it against.

`id` absent → **Creation/Enrichment** shape: the Character is matched — or, if genuinely new,
created — via ADR 013's own Type-anchored, Series-scoped algorithm using `name` + `sourceTitle` +
`sourceType`, the identical identity test `ResolveCharacterAsync` already applies per-quote, just
decoupled from any specific quote's own text. Purely additive — a file without a `characters`
section parses identically to today.

### 2. `CharacterEntry.cs` DTO and reader wiring

**Status:** Done.

New `src/Quotinator.Core/Import/CharacterEntry.cs` record, doc-commented like
`SourceEntry`/`PersonEntry`. Widened per step 1's resolved design: `Id` (nullable `string?`),
`Name` (`required string`), `SourceTitle` (`required string`), `SourceType` (`QuoteType`, defaults
`Movie`, `[JsonConverter(typeof(QuoteTypeJsonConverter))]`). `ParsedSourceFile` gained a
`Characters` property (defaults `[]`). `SourceQuoteFileReader.TryParseExtended` gained the new
root-key parse, matching the existing section pattern.

### 3. `Sql.Characters` new queries

**Status:** Done.

Add to the `Sql.Characters` nested class in `src/Quotinator.Core/Queries/Sql.cs`:
- `SelectExistingById` — `SELECT Name, CompletenessStatus FROM Characters WHERE Id = @id AND IsDeleted = 0;`
  (mirrors `Sql.Sources.SelectExistingById`'s shape, minus `Title`/`Type`/`Date`). Correctly scoped
  to id-only lookup — `SourceType` is immutable after creation (ADR 013 Decision 9's remark on
  `EnsureCharacterExistsAsync`) and this issue never exposes it as a Modify-able field, so this
  query doesn't need to read or compare it.
- `UpdateFieldsById` — `UPDATE Characters SET Name = @name, DateModified = @dateModified WHERE Id = @id;`
  (mirrors `Sql.Sources.UpdateFieldsById`; never touches `CompletenessStatus`/`NoValueKnown` — see
  its own remark on `UpdateCompletenessById` for why that's separate).
- `SelectCompletenessById` / `UpdateCompletenessById` — same shape as `Sql.Sources`'s and
  `Sql.Quotes`'s own pair, used by `ApplyCompletenessAsync`'s existing before/after read-and-write.

**This step's own original assumption was wrong, found during this review (2026-07-24):** it
previously said "#174 has already added `Sql.Characters.SelectIdByName`... this issue only adds the
four id-keyed queries above, not the natural-key one." #174 did not add any such method. It added
`Sql.Characters.SelectGlobalCandidateId(sourceId, name, sourceType, seriesId)` (ADR 013 Decision 7)
— a lookup that requires Source context, not a bare Name lookup, because Character's identity is no
longer simply global-by-Name the way Person's is (two different Characters can legitimately share a
Name when no Series connects their Sources — ADR 013 Decision 5/6). **This step adds only the four
id-keyed queries listed above — `SelectGlobalCandidateId` already exists from #174 and is reused
directly by step 4's widened-schema design (developer decision, 2026-07-24), not reimplemented
here.**

### 4. `PlanCharactersAsync` in `ImportActionPlanner.cs`

**Status:** Done.

**Design decided 2026-07-24, resolving the open question this section previously raised (developer
chose "widen the schema" — see Notes).** For each `CharacterEntry c` (id canonicalized via
`EntityIdCanonicalizer.TryCanonicalizeLowercase` when present), `sourceTypeStr = c.SourceType.
ToString()`:

1. **If `c.Id` is present**, look up `Sql.Characters.SelectExistingById(c.Id)`.
   - **Found** → `matchedId = c.Id`. Proceed to the shared name-diff/policy-resolution/
     `CompletenessGuard.ShouldBlock` flow (step 2 below) — the Correction path, `sourceTitle`/
     `sourceType` are read from the file but never diffed or written (immutable per ADR 013
     Decision 9).
   - **Not found** → fall through to natural-key resolution below, mirroring `PlanSourcesAsync`'s
     own "id declared but doesn't match anything" fallback-to-natural-key behaviour
     (`PlanSourcesAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged`).
2. **If `c.Id` is absent, or fell through from a non-match above** — this is where the widened
   schema earns its keep:
   - Resolve `resolvedSourceId` for `(c.SourceTitle, sourceTypeStr)`: check `sourceIndex` first (a
     `sources[]` entry earlier in the same file, or an already-processed quote may already have
     resolved it), then `Sql.Sources.SelectIdByTitleAndType`, then — if genuinely new — stage a
     Source `Add` via `EntityIdentity.SourceId`. This mirrors `PlanSourcesAsync`'s own
     natural-key-fallback logic in shape. Implemented as a new, standalone
     `ResolveOrStageSourceIdAsync` helper rather than a true extraction from `PlanSourcesAsync`
     itself — `PlanSourcesAsync`'s own Date/SeriesId diff logic is complex enough that reshaping it
     into a shared helper risked destabilising it for a change this issue doesn't otherwise need to
     touch; revisit as a real extraction only if a third caller needs the same logic.
   - `seriesId = await Sql.Sources.SelectSeriesIdById(resolvedSourceId)` — `NULL` for a Source just
     staged fresh above, which is the correct, conservative-by-default answer (ADR 013 Decision 8).
   - `candidateId = await Sql.Characters.SelectGlobalCandidateId(resolvedSourceId, c.Name,
     sourceTypeStr, seriesId)`.
     - **Found** → `matchedId = candidateId`. Proceed to the shared name-diff flow (step 2 below) —
       matches ADR 013's real identity test, not a fictional Name-only lookup.
     - **Not found** → genuinely new. Stage an `Add` using `canonicalId ?? EntityIdentity.
       CharacterId(resolvedSourceId, c.Name, sourceTypeStr)` and `CharacterActionPayload(
       resolvedSourceId, c.Name, c.SourceTitle, sourceTypeStr)` — identical shape to
       `ResolveCharacterAsync`'s own Add path, so the existing apply-time machinery
       (`EnsureCharacterExistsAsync`, unchanged since ADR 013 Decision 9) needs no new logic to
       create both the Character row and its `CharacterSources` link. Index
       `characterIndex[$"{resolvedSourceId}|{c.Name}"] = stableId` so a same-batch quote referencing
       this exact (Source, Name) pair resolves to it — mirroring the existing `sourceIndex`
       precedent from #162. **Found live via T2, not the unit suite:** the first implementation
       pass unconditionally used the `EntityIdentity`-derived id here, silently discarding a
       supplied-but-unmatched explicit id — diverging from `PlanSourcesAsync`'s own established
       `canonicalId ?? EntityIdentity.SourceId(...)` precedent. Two unit tests
       (`ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_HardDeletesCorrectly`,
       `ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_SoftDeletesCorrectly`) were written
       against this exact scenario and initially failed — not on a case-sensitivity assertion, but
       because the row they queried for by the file's own id genuinely didn't exist under that id.
       Fixed by matching Source's precedent exactly.
3. **Shared name-diff flow** (both branches above land here once `matchedId` is known): field-map
   diff on `name` only → unchanged early-continue (no action) → policy-based resolution →
   `CompletenessGuard.ShouldBlock` evaluated against the policy-**resolved** value (#168's rule) →
   stage `Blocked`/`Modify`/`Pending` per policy — exactly `PlanPeopleAsync`'s own shape.

**Case-insensitivity policy correction, decided live during this review (2026-07-24) — scope
extends beyond #175 itself.** The natural-key Source resolution above calls `Sql.Sources.
SelectIdByTitleAndType`, which is currently case-**sensitive** by deliberate, documented policy
("free-text natural-key values, not identifiers"). Developer decision: this was wrong — any input
originating from an import file, and the stable ids this project generates from it, must be
case-insensitive, so classifying an entry as "new" vs. "already exists" carries minimal friction and
never risks a case-only duplicate. `Sql.Sources.SelectIdByTitleAndType` and its sibling
`SelectExistingByTitleAndType` both switch to `LOWER(...)`-wrapped Title/Type comparison — this is a
correction to existing #162/#180 behaviour (`PlanSourcesAsync`, `ResolveSourceAsync`), not new
#175-only logic, so both call sites' existing tests need re-auditing for a newly-case-insensitive
result, and the doc comment explaining the old case-sensitive rationale needs rewriting, not just
deleting. `Character.Name` (via `SelectGlobalCandidateId`) was already case-insensitive from #174.

**Deliberately out of scope, even under the widened schema:** if a `characters[]` entry resolves to
an *existing* Character (via id or via `SelectGlobalCandidateId`) that isn't yet linked to
`c.SourceTitle`'s specific Source, this step does **not** stage anything to create that
`CharacterSources` link — a found match takes the same "nothing to do beyond the name diff" path
Source's own unchanged-match already takes. Establishing a *new* cross-Source link for an
already-existing Character remains exclusively a side effect of a real Quote resolving through
`ResolveCharacterAsync` (whose apply-time defensive re-ensure already handles it). Widening the
schema was about giving the Enrichment/Creation path a *real* identity test to run, not about
letting a standalone `characters[]` entry manage links independent of any quote — that would be a
materially bigger feature than "explicit id, Modify/decidability" describes. Revisit only if a
concrete need for it shows up later, not preemptively.

A character discovered only implicitly through `SourceQuote.Character` (no matching
`characters[]` entry) is never touched by this method and stays Add-only via the existing
`ResolveCharacterAsync`, exactly like a Person discovered only through `SourceQuote.Author`.

### 5. `ApplyResolvedActionAsync`'s Character case — Add/Modify split

**Status:** Done.

`SqliteImportActionService.ApplyResolvedActionAsync`'s `case ImportActionEntityTypes.Character`
block branches on `action.ActionType`: `Add` keeps the existing `EnsureSourceExistsAsync` +
`EnsureCharacterExistsAsync` calls (#174's own shape, unchanged by this issue) unchanged; `Modify`
deserializes `action.MergedFields` and calls the new `Sql.Characters.UpdateFieldsById`, then applies
`ApplyCompletenessAsync` the same way Source's Modify branch does.

### 6. `DecideAsync`'s Character Modify branch

**Status:** Done.

Add an `EntityType == Character && ActionType == Modify` branch to
`SqliteImportActionService.DecideAsync`, mirroring the existing Source Modify branch: deserialize
`ExistingValue`/`IncomingValue` as the (post-#174) Character payload type, build single-field
(`name`) field maps, resolve via `FieldMergeResolver.ResolveWithDecisions` using the new
`request.CharacterName` decision, and pass the resolved payload to `_coordinator.DecideAsync`.

### 7. `ComputeAmbiguousFields`'s Character case

**Status:** Done.

Add a `case ImportActionEntityTypes.Character` arm to `ComputeAmbiguousFields`, alongside the
existing `Quote`/`Source` arms — same single-field map built from the post-#174 Character payload,
fed into the same `FieldMergeResolver.ResolveWithDecisions`/`UnresolvedFieldConflictException`
pattern the existing two arms already use.

### 8. `ReverseAppliedActionsAsync`'s Character case — Add/Modify split

**Status:** Done.

Split the existing `case ImportActionEntityTypes.Character` block on `action.ActionType`: `Modify`
restores `Name` via `Sql.Characters.UpdateFieldsById` from `ExistingValue`, with no active-reference
check (a Modify reversal never deletes anything) — same shape as the existing Source `Modify` branch
in this same method. `Add` keeps its existing active-reference-check-then-soft-delete behaviour via
`_characterRepository.SoftDeleteAsync(Guid.Parse(action.EntityId), uow)`, unchanged.

**Re-verified live, not assumed — the raw-SQL switch #162/#173 needed for Source/Person is not
needed for Character.** `#173`'s own plan doc made the same "keep today's behaviour unchanged"
assumption and was wrong at the time — `SoftDeleteAsync` had the same case-sensitivity bug as
`ClearStaleAddTargetsAsync` (step 9) because `GuidHandler` then force-*uppercased* its parameter.
That bug no longer exists: `SqliteRepository<T>.SoftDeleteAsync`/`SqliteRestorableRepository<T>.
HardDeleteAsync` now call `id.ToCanonicalId()` before binding, and the `RepositorySql.SoftDelete`/
`HardDelete` queries they call wrap the id comparison in `IdClauses.Equals` — both fixed by this
session's #200–205 (`RepositorySql` → `IdClauses`) and #176–178 (`ToCanonicalId()` choke point)
work, which postdates #173's own incident. Confirmed, not just traced:
`ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_SoftDeletesCorrectly` was written against the
unmodified Guid-typed repository path and passed outright (once the unrelated Add-id bug described
in step 4 was fixed) — no raw-SQL switch was made here.

### 9. `ClearStaleAddTargetsAsync` raw-SQL fix and `ConflictDecisionRequest.CharacterName`

**Status:** Done.

`ClearStaleAddTargetsAsync`'s Character cleanup loop kept its existing Guid-typed repository path
unchanged:

```csharp
foreach (var action in adds.Where(a => a.EntityType == ImportActionEntityTypes.Character))
{
    await quoteConn.ExecuteAsync(Sql.CharacterSources.DeleteForCharacter, new { id = action.EntityId });
    await _characterRepository.HardDeleteAsync(Guid.Parse(action.EntityId));
}
```

**Re-verified live, not assumed — no raw-SQL switch was needed, unlike the fix #162 made for Source
and #173 made for Person.** `SqliteRestorableRepository<T>.HardDeleteAsync` calls
`id.ToCanonicalId()` before binding, and `RepositorySql.HardDelete` wraps its comparison in
`IdClauses.Equals` — both fixed since #173's own incident by this session's #200–205/#176–178 work.
Confirmed, not just traced: `ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_
HardDeletesCorrectly` was written against the unmodified repository-path code and passed outright
(once the unrelated Add-id bug described in step 4 was fixed).

Added `CharacterName` (nullable `FieldDecision?`) to `ConflictDecisionRequest.cs`, alongside the
existing `PersonName`/`SourceTitle` properties, and wired it into the new `ToCharacterDecisionMap`
helper used by steps 6/7 above.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A file without a `characters` section parses identically to today | Unit test | `SourceQuoteFileReaderTests.SourceQuoteFileReader_NoCharactersSection_DefaultsToEmpty` |
| 2 | ✅ | An id-match with a differing `name` stages a `Modify` action | Unit test | `ImportActionPlannerTests.PlanCharactersAsync_IdMatchFound_NameDiffers_StagesModifyAction` |
| 3 | ✅ | An id-match with nothing changed stages no action | Unit test | `ImportActionPlannerTests.PlanCharactersAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 4 | ✅ | No-id-match behaviour resolves via ADR 013's real algorithm (same-Source candidate, series-scoped candidate, no candidate → Add, Source itself missing → both staged) | Unit test | `ImportActionPlannerTests.PlanCharactersAsync_IdDoesNotMatch_FallsBackToSameSourceCandidate_NoActionStaged`, `PlanCharactersAsync_NoIdMatch_SeriesScopedCandidateFound_NoActionStaged`, `PlanCharactersAsync_NoIdMatch_NoCandidateFound_StagesAddAction`, `PlanCharactersAsync_NoIdMatch_SourceDoesNotExistYet_StagesBothSourceAndCharacterAdds` |
| 5 | ✅ | A `Complete`-status id-matched row stages `Blocked`, not `Modify` | Unit test | `ImportActionPlannerTests.PlanCharactersAsync_CompleteStatus_StagesBlockedNotModify` |
| 6 | ✅ | A `Complete`-status row under `Skip` policy never blocks (#168 rule) | Unit test | `ImportActionPlannerTests.PlanCharactersAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 7 | ✅ | Decide endpoint accepts a Character `Modify` field decision | Unit test | `SqliteImportActionServiceTests.DecideAsync_CharacterModify_ResolvesFieldDecisions` |
| 8 | ✅ | Reversing a Character `Modify` restores `ExistingValue`'s `Name` | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_CharacterModify_RestoresExistingValue` |
| 9 | ✅ | A lowercase-authored explicit Character id hard-deletes correctly on stale-Add cleanup | Unit test | `SqliteImportActionServiceTests.ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_HardDeletesCorrectly` |
| 10 | ✅ | A lowercase-authored explicit Character id soft-deletes correctly when its `Add` action is reversed | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_SoftDeletesCorrectly` |
| 11 | ✅ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s); `dotnet test --configuration Release` → 1016/1016 (Core), 614/614 (Data), 496/496 (Api), all other projects passing |
| 12 | ✅ | Live: a `characters[]` correction is staged/decided/applied via `POST /api/v1/import`, a `Complete` Character's `name` cannot be silently overwritten, an explicit unmatched id is honoured on Add, and a differently-cased `sourceTitle` resolves to the existing Source without a duplicate | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .` — see CLAUDE.md's T2 checklist, "#175" section, for the full command sequence and expected responses |
| 13 | ✅ | App still opens and builds in Visual Studio after the schema/migration surface from #174 this issue builds on | Live (T1) | Developer's own Visual Studio pass — clean startup, schema v11 (data v10) unchanged as expected (this issue adds no migration), 799 quotes / 482 sources / 7 characters / 3 people |

---

## Notes

T1 and T2 are both required — per this project's blanket rule (no exemption for a change with real
C# logic and a data-model surface).

**This plan doc's original Background/spec framing assumed Character had become a flat global
entity keyed by `Name`, matching Person's own shape exactly — that premise was false as shipped by
#174.** #174's ADR 013 settled on a Type-anchored, Series-scoped identity instead: two Characters
with the same Name can legitimately remain separate rows when their Sources don't share a Series.
`Sql.Characters.SelectGlobalCandidateId(sourceId, name, sourceType, seriesId)` requires Source
context a bare id+name `characters[]` entry (this plan doc's original step 1 design) has no way to
supply. Resolved by widening the schema (developer decision, 2026-07-24) to carry `sourceTitle`/
`sourceType` unconditionally — see Step 1. `CharacterActionPayload` needed no changes (ADR 013
Decision 9 — `SourceType` is immutable once a Character exists). See `docs/architecture-decisions/
013-character-merge-algorithm.md` for the full algorithm.

**Reconciled against #173's actual shipped implementation.** `PlanPeopleAsync`'s id-match/
policy-resolution/`CompletenessGuard.ShouldBlock` shape was the direct template for
`PlanCharactersAsync`'s shared flow (step 4), `ConflictDecisionRequest`'s per-field-decision naming
convention carried over (`PersonName` → `CharacterName`), and the `ApplyCompletenessAsync` wiring in
the `Modify` apply branch (step 5) matched exactly.

**The raw-SQL, case-preserving fix #162 made for Source and #173 made for Person turned out
unnecessary for Character — confirmed by a passing test against unmodified code, not assumed from a
code trace.** By the time this issue was implemented, this session's own #200–205 (`RepositorySql` →
`IdClauses`) and #176–178 (`ToCanonicalId()` choke point) work had already made the Guid-typed
repository path (`SqliteRepository<T>.SoftDeleteAsync`, `SqliteRestorableRepository<T>.
HardDeleteAsync`) case-insensitive end to end. Steps 8 and 9 were both implemented by first writing
their verification test against the *unmodified* code and only reaching for a raw-SQL switch if that
test failed — it didn't, so `ClearStaleAddTargetsAsync` and `ReverseAppliedActionsAsync`'s Character
branches both still use the Guid-typed repository calls.
