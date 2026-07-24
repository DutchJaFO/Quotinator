# #175 — Character: explicit id, Modify/decidability

**Status:** Planning
**GitHub issue:** #175
**Tiers required:** T1, T2
**Depends on:** #174

---

## Spec requirements (from the GitHub issue)

1. `schemas/source-extended.schema.json` gains a `characters` array + `character` `$def`: `id`
   (required, UUID v4 pattern), `name` (required). No `sourceTitle`/`sourceType` — #174 makes
   Character source-independent, so a character entry no longer links to any specific Source.
2. New `CharacterEntry.cs` record in `Quotinator.Core.Import`, doc-commented like
   `PersonEntry`/`SourceEntry`. `ParsedSourceFile` gains `Characters` (defaults `[]`).
   `SourceQuoteFileReader.TryParseExtended` gains the new root-key parse.
3. `Sql.Characters` (`src/Quotinator.Core/Queries/Sql.cs`) gains `SelectExistingById` (returns
   `Name`, `CompletenessStatus`), `UpdateFieldsById`, `SelectCompletenessById`,
   `UpdateCompletenessById`.
4. New `PlanCharactersAsync` (`src/Quotinator.Core/Database/ImportActionPlanner.cs`), mirroring
   #173's `PlanPeopleAsync` shape exactly: id-match lookup → field-map diff (`name` only) →
   unchanged-check → policy-based resolution → `CompletenessGuard.ShouldBlock` evaluated against
   the policy-**resolved** value → stage `Blocked` or `Modify`. **The natural-key fallback for a
   no-id-match case is an open design question, not settled — see Notes.** #174 did not add a
   simple `Sql.Characters.SelectIdByName`; it added `Sql.Characters.SelectGlobalCandidateId`, which
   requires `sourceId`/`sourceType`/`seriesId` (ADR 013) — parameters a bare `characters[]` entry
   (id + name only, per step 1) has no way to supply. A character discovered only implicitly
   through a Quote's `character` string (no explicit `characters[]` entry) stays Add-only forever,
   same rule as Person.
5. `ApplyResolvedActionAsync`'s Character case splits on `ActionType`: `Add` unchanged; `Modify`
   calls the new `Sql.Characters.UpdateFieldsById`.
6. `DecideAsync` gains an `EntityType == Character && ActionType == Modify` branch.
7. `ComputeAmbiguousFields` gains/updates the `Character` case for the new global shape.
8. `ReverseAppliedActionsAsync`'s Character case splits on `ActionType`: `Add` keeps
   soft-delete-if-unreferenced; `Modify` restores `Name` via `UpdateFieldsById` from
   `ExistingValue`.
9. `ClearStaleAddTargetsAsync`'s Character cleanup branch switches from the Guid-typed repository
   path to the raw-SQL, case-preserving pattern — same fix #162 made for Source and #173 made for
   Person — needed because an explicit `characters[]` id is file-authored and not guaranteed
   canonically cased. **Whether this switch is still actually necessary for Character, given fixes
   this session made to the underlying repository/query layer, is now an open question — see step
   9's own note.**
10. `ConflictDecisionRequest` gains `CharacterName` (nullable `FieldDecision?`).

---

## Steps

### 1. Schema: `characters` array + `character` `$def`

**Status:** Not started.

Add a top-level `characters` array to `schemas/source-extended.schema.json` (same shape/precedent
as `sources`/`stageDirections`/`soundCues`/`conversations`) referencing a new `character` `$def`
under `$defs`. Fields: `id` (required, UUID-v4 pattern, same regex as the other explicit-id
`$def`s) and `name` (required). Deliberately **no** `sourceTitle`/`sourceType`/any Source linkage —
Character is source-independent as of #174, mirroring the `person` `$def`'s shape (already shipped
by #173, confirmed present in the schema during this review) rather than the `source` `$def`'s
shape. Purely additive — a file without a `characters` section parses identically to today. **This
id+name-only shape is exactly what step 4's Notes flag as the source of the open no-id-match design
question — revisit this step too if that question resolves toward widening the schema.**

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
Name when no Series connects their Sources — ADR 013 Decision 5/6). **This step alone doesn't
resolve step 4's fallback question — see Notes for why a real design decision is needed before
implementation starts, not just a rename.**

### 4. `PlanCharactersAsync` in `ImportActionPlanner.cs`

**Status:** Not started.

New private method mirroring `PlanSourcesAsync`'s control flow (id-match → field diff → unchanged
early-continue → policy-resolved value → `CompletenessGuard.ShouldBlock` against the *resolved*
diff, per #168's rule → `Blocked`/`Modify`/`Pending` per policy), but with a single-field
(`name`-only) field map instead of Source's three-field one — the same simplification #173's
`PlanPeopleAsync` made relative to `PlanSourcesAsync`. Called from `PlanAsync` alongside the
existing `PlanSourcesAsync`/`PlanStageDirectionsAsync`/etc. calls, indexing into the same
`characterIndex` dictionary `ResolveCharacterAsync` already populates/consults, so a same-batch
quote referencing an explicitly-declared character resolves to the corrected id (same gap
`PlanSourcesAsync_QuoteReferencesExplicitlyDeclaredSource_ResolvesToItsId` caught for Source in
#162).

**No-id-match fallback — open design question, not decided by this plan doc (found during this
review, 2026-07-24):** the original text here said this method "falls back to
`Sql.Characters.SelectIdByName`... when no id-match, same natural-key-fallback contract
`PlanSourcesAsync` already has." That method doesn't exist, and a simple Name-only lookup would be
semantically wrong even if it did — see step 3's note. Concretely, when a `characters[]` entry's
declared id matches no existing row, `PlanCharactersAsync` has no Source/Type/Series context to run
ADR 013's real matching algorithm against, because step 1's schema (id + name only) never carries
that context. Two candidate resolutions, neither decided here:
  - Drop the natural-key fallback entirely — a `characters[]` entry is only ever for *correcting* an
    already-known Character (this issue's own title: "explicit id, Modify/decidability"), never for
    creating one; a Character can only ever come into existence via `ResolveCharacterAsync`'s
    Source-driven flow (`EnsureCharacterExistsAsync` always requires a `sourceId`/`sourceType`). An
    id that matches nothing would then be an error or a no-op, not an Add.
  - Widen step 1's schema to carry enough Source/Series context for a `characters[]` entry to
    participate in real ADR 013 matching — a much bigger scope change than this issue currently
    describes.
  Whoever picks this issue up next must settle this before writing `PlanCharactersAsync`, not
  assume the original "same as Source/Person" framing still holds.

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

**`Add` is *not* unchanged — it also needs the raw-SQL, case-preserving fix — but this whole
premise needs live re-verification before implementing, not blind trust in this text (found during
this review, 2026-07-24, and deliberately not resolved here).** #173's own plan doc originally said
the same thing this step used to say ("keep today's soft-delete-if-unreferenced behaviour
unchanged") and was wrong: found live via T2 that
`_personRepository.SoftDeleteAsync(Guid.Parse(action.EntityId), uow)` had the identical
case-sensitivity bug as `ClearStaleAddTargetsAsync` (step 9) at the time #173 shipped — `GuidHandler`
then force-*uppercased* the parameter (stale direction; #210's later system-wide lowercase revision
flipped this to force-*lowercase*, per ADR 012's final form).

**Traced live during this review, not just corrected for casing direction:** `SqliteRepository<T>.
SoftDeleteAsync`/`SqliteRestorableRepository<T>.HardDeleteAsync` (`src/Quotinator.Data/Repositories/
SqliteRepository.cs`, `SqliteRestorableRepository.cs`) already call `id.ToCanonicalId()` explicitly
before binding, and the `RepositorySql.SoftDelete`/`HardDelete` queries they call already wrap the
id comparison in `IdClauses.Equals` (case-insensitive `LOWER(...) = LOWER(...)`) — both fixed by
this session's #200–205 (`RepositorySql` → `IdClauses`) and #176–178 (`ToCanonicalId()` choke point)
work, which postdates #173's own incident. Tracing the code suggests the Guid-typed repository path
may now be safe regardless of input casing — but this is a code trace, not a passing test, and this
plan doc has already been burned once by an untested assumption in this exact spot (that's the whole
reason step 8 exists in the first place). **Do not silently keep or silently remove the raw-SQL
switch based on this trace — write `ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_
SoftDeletesCorrectly` (verification row 10) against the *unmodified* repository-path code first; if
it already passes, the raw-SQL switch below may be unnecessary for Character specifically (though
still worth keeping for consistency with Source/Person's own already-shipped fix) — record the
actual result before deciding, don't assume either way.** If a raw-SQL switch is still made, use:
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
currently `EntityIdentity`-derived (always canonical by construction, per ADR 012 — corrected from
this text's original "always uppercase," stale since #210's system-wide lowercase revision). An
explicit `characters[]` id is file-authored and not guaranteed canonically cased, the same gap #162
found and fixed for Source and #173 has now actually fixed for Person (`SqliteImportActionService.cs`'s
`ClearStaleAddTargetsAsync` Person branch, committed `8756b37`).

**Same re-verification caveat as step 8 above, traced during this review (2026-07-24) — do not
assume the raw-SQL switch is still needed without checking.** `SqliteRestorableRepository<T>.
HardDeleteAsync` already calls `id.ToCanonicalId()` before binding, and `RepositorySql.HardDelete`
already wraps its comparison in `IdClauses.Equals` — both fixed since #173's own incident. The
"proposed fix" below binds `action.EntityId` (already canonicalized at #175's own capture point, per
ADR 012) as a raw string with no further C#-side canonicalization, relying entirely on
`IdClauses.Equals`'s SQL-level `LOWER()` wrapping — which the repository path *also* goes through,
via the same `RepositorySql.HardDelete` string. Tracing the code, both paths appear equally safe
today; write `ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_HardDeletesCorrectly`
(verification row 9) against the *unmodified* repository-path code first and record the actual
result, same as step 8. If a raw-SQL switch is still made, use the pattern already used for
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
| 2 | ❌ | An id-match with a differing `name` stages a `Modify` action | Unit test | `Quotinator.Core.Tests.PlanCharactersAsync_IdMatchFound_NameDiffers_StagesModifyAction` |
| 3 | ❌ | An id-match with nothing changed stages no action | Unit test | `Quotinator.Core.Tests.PlanCharactersAsync_IdMatchFound_NothingChanged_NoActionStaged` |
| 4 | ❌ | No-id-match behaviour — **not decided, see step 4's Notes** | Unit test | Test name/shape depends on which of step 4's two open options is chosen; `PlanCharactersAsync_NoIdMatch_FallsBackToNaturalKey_NoActionStaged` (the original name) assumed a resolution this review found unsupported by #174's actual implementation |
| 5 | ❌ | A `Complete`-status id-matched row stages `Blocked`, not `Modify` | Unit test | `Quotinator.Core.Tests.PlanCharactersAsync_CompleteStatus_StagesBlockedNotModify` |
| 6 | ❌ | A `Complete`-status row under `Skip` policy never blocks (#168 rule) | Unit test | `Quotinator.Core.Tests.PlanCharactersAsync_CompleteStatus_SkipPolicy_DoesNotBlock` |
| 7 | ❌ | Decide endpoint accepts a Character `Modify` field decision | Unit test | `Quotinator.Core.Tests.DecideAsync_CharacterModify_ResolvesFieldDecisions` |
| 8 | ❌ | Reversing a Character `Modify` restores `ExistingValue`'s `Name` | Unit test | `Quotinator.Core.Tests.ReverseBatchAsync_CharacterModify_RestoresExistingValue` |
| 9 | ❌ | A lowercase-authored explicit Character id hard-deletes correctly on stale-Add cleanup | Unit test | `Quotinator.Core.Tests.ClearStaleAddTargetsAsync_CharacterExplicitLowercaseId_HardDeletesCorrectly` |
| 10 | ❌ | A lowercase-authored explicit Character id soft-deletes correctly when its `Add` action is reversed (the second of #173's two-call-site case-sensitivity fix — see step 8) | Unit test | `Quotinator.Core.Tests.ReverseBatchAsync_CharacterAdd_ExplicitLowercaseId_SoftDeletesCorrectly` |
| 11 | ❌ | Build clean, full suite green | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s); `dotnet test --configuration Release` → all projects passing |
| 12 | ❌ | Live: a `characters[]` correction is staged/decided/applied via `POST /api/v1/import`, and a `Complete` Character's `name` cannot be silently overwritten | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .`, same shape as #162's own T2 row: stage/decide/apply an explicit-`characters[]` `Modify`; separately confirm a `Complete` Character under `Skip` policy is not blocked; additionally, exercise a lowercase-id Add → reverse → re-add cycle live and confirm the row's `IsDeleted` flag actually flips (not just assumed) between steps, matching #173's own T2 canary for this exact bug class |
| 13 | ❌ | App still opens and builds in Visual Studio after the schema/migration surface from #174 this issue builds on | Live (T1) | Developer's own Visual Studio pass — app starts cleanly, database reset/reseed both succeed |

---

## Notes

T1 and T2 are both required — per this project's blanket rule (no exemption for a change with real
C# logic and a data-model surface).

**#174 has now landed (2026-07-24) — this issue is unblocked, but its actual shipped shape differs
from what this plan doc originally assumed, and one open design question blocks starting
implementation.** This plan doc's own Background/spec framing said "once Character is global (no
`SourceId`), this issue is structurally a near-copy of Person's own... issue — no linkage/scoping
design question remains, since Character now shares Person's exact shape — a single global entity
keyed by `Name`." **That premise is false as shipped.** #174's ADR 013 settled on a
Type-anchored, Series-scoped identity, not a flat global-by-Name one like Person's: two Characters
with the same Name can legitimately remain separate rows when their Sources don't share a Series.
`EntityIdentity.CharacterId` gained a third parameter (`sourceType`), not the two-parameter
`(name, sourceType)` this plan doc's own step 3/4 originally speculated. `Sql.Characters` gained
`SelectGlobalCandidateId(sourceId, name, sourceType, seriesId)`, not a simple `SelectIdByName`.
`CharacterActionPayload` needed **no changes at all** (ADR 013 Decision 9) — the plan doc's step 5
already correctly anticipated this as a possible outcome ("this step assumes that shape is already
in place"), so no correction was needed there. See `docs/architecture-decisions/
013-character-merge-algorithm.md` for the full algorithm.

**The consequence for this issue: step 4's no-id-match fallback is a genuine open design question,
not a mechanical port of Person's shape — see step 4's own note.** A bare `characters[]` entry (id +
name only, per step 1) carries no Source/Type/Series context, so it cannot run ADR 013's real
matching algorithm if its declared id doesn't match an existing row. Whoever picks this issue up
next must settle this — drop the fallback entirely (an unmatched id is an error/no-op, since Character
can now only ever be *created* via `ResolveCharacterAsync`'s Source-driven flow) or widen step 1's
schema to carry Source/Series context — before writing `PlanCharactersAsync`.

**Also found during this review: steps 8/9's prescribed raw-SQL fix may already be moot, not just
mis-explained.** Tracing `SqliteRepository<T>.SoftDeleteAsync`/`SqliteRestorableRepository<T>.
HardDeleteAsync` shows both already call `id.ToCanonicalId()` before binding, and the
`RepositorySql.SoftDelete`/`HardDelete` queries they call already wrap the id comparison in
`IdClauses.Equals` — both fixed by this session's #200–205/#176–178 work, which postdates #173's own
incident this plan doc's steps 8/9 are built on. This is a code trace, not a passing test — see
steps 8/9's own notes for the exact re-verification each needs before deciding whether the raw-SQL
switch is still necessary for Character.

**Reconciled against #173's actual shipped implementation (2026-07-14).** This plan doc's original
projection matches what #173 actually shipped on every point already checked:
`PlanPeopleAsync`'s id-match/natural-key-fallback/`personIndex`-threading shape (direct template for
`PlanCharactersAsync`, step 4 — though the fallback's own target query turned out different, see
above), `ConflictDecisionRequest`'s per-field-decision naming convention
(`PersonName`/`PersonDateOfBirth`/`PersonDateOfDeath` → this issue's own `CharacterName`), the
`ToPersonDecisionMap`/`ToFieldMap` helper pattern, plain-`string?` payload fields (never `SafeValue`,
matching `Source.Date`'s convention), and the `ApplyCompletenessAsync` wiring in the `Modify` apply
branch (step 5).
