# #68 — Curated JSON: conversations format

**Status:** Waiting for release
**GitHub issue:** #68
**Tiers required:** T1, T2
**Depends on:** #67, #61, #58, #154

---

## Scope changes

Cross-checked against current code before planning (per `process.md`'s mandatory pre-implementation
check). The issue as filed on 2026-06-16 says "`DatabaseInitializer` seeds `Conversations`, ...
from the new sections" — written before #58 (`ImportBatches` provenance), #149 (conflict-review
workflow), and #154 (unified staging engine) existed. Today, `Quotes` are never written directly by
`DatabaseInitializer` on its own — both startup seeding and the live import endpoint (`POST
/api/v1/import`) go through the same shared `QuoteSeedWriter`, and every write is staged through
`System_ImportActions` (`Quotinator.Data`) for conflict handling before being applied.

**Correction (confirmed with the user):** conversations/stageDirections/soundCues follow the same
path. A new `ConversationSeedWriter` (`Quotinator.Engine.Database`, sibling to `QuoteSeedWriter`) is
shared between `QuotinatorDatabaseInitializer` and the live import endpoint, staged through
`System_ImportActions` the same way. This requires no `Quotinator.Data` change —
`SystemImportAction.EntityType` is already free-text and caller-defined (see ADR 008's discussion of
this field), so `Quotinator.Engine` registering `"Conversation"`, `"StageDirection"`, and
`"SoundCue"` as new entity-type values needs no upstream change. This is why #58 and #154 are added
as explicit dependencies above — both are already `Waiting for release` in this same milestone, so
this is a scope correction, not a new blocker.

Consequence: conversations become reachable through `POST /api/v1/import` as well as startup
seeding, and gain the same duplicate/conflict handling every other imported entity gets — not a
separate, bespoke seeding path as the literal issue text implied.

`schemas/source-extended.schema.json` already exists (added alongside #61, 2026-06-18) and already
matches this issue's format almost exactly — it was never wired up to an actual writer. This issue
is "finish wiring the schema that already exists," not "design a new one."

**Scope correction 2 (mid-implementation, confirmed with the user):** the curated file's example
content was expanded beyond the single Airplane! conversation the issue originally scoped —
requested explicitly so the seeded example data actually exercises `StageDirection` and `SoundCue`
lines, not just `Quote` lines (a file with only `Quote`-type lines would never prove those two code
paths work end to end). Four conversations now: Airplane! (unchanged, quotes only), Monty Python and
the Holy Grail's Black Knight scene (one `sound_cue` line + 3 quotes), The Princess Bride's Inigo
Montoya confrontation (one `stage_direction` line + 1 quote), and The Empire Strikes Back's "I am
your father" scene (one `stage_direction` line + 4 quotes). Every quote's wording was verified
against the actual film dialogue via web search before being added, per the project's "never
generate or invent quotes" priority — stage direction/sound cue text is a plain scene descriptor
(the schema's own example, `"[EXT. AIRPORT - DAY]"`, is not verbatim screenplay text either), not a
quotation, so it doesn't carry the same verification requirement.

**Scope correction 3 (mid-implementation, confirmed with the user):** Section 3 below turned out to
be substantially larger than originally estimated once the actual apply/reverse pipeline
(`SqliteImportActionService.cs`, ~650 lines) was read in full — it's a hand-rolled per-entity-type
state machine (planning, dependency ordering, decide, apply, reverse), not a simple writer function.
The user chose to keep the original "full staging engine integration" design rather than descope to
a simpler direct-write path. Implemented in a follow-up session (not the same session the design was
written in) — see section 3 for what shipped, including two correctness issues the design pass
hadn't anticipated and only surfaced while implementing (the `SelectAllForBatch` ordering gap and the
`CountActiveReferences` join bug, both described there).

---

## Spec requirements (as corrected)

1. Source files support the extended object format already defined in
   `schemas/source-extended.schema.json`: `{ "quotes": [...], "stageDirections": [...],
   "soundCues": [...], "conversations": [...] }`. The flat top-level array format remains valid
   (treated as `{ "quotes": [...] }`).
2. `quotinator-curated.json` migrated from a flat array to the extended object format, with four
   real conversations — see Scope correction 2 above for the full list and rationale.
3. Parsing goes through typed DTOs per the project's JSON parsing policy — no manual `JsonNode`
   walking beyond the single top-level shape sniff already established in
   `SourceQuoteFileReader.TryParse`.
4. Seeding writes `Conversations`, `ConversationLines`, `StageDirections`,
   `StageDirectionTranslations`, `SoundCues`, `SoundCueTranslations` through the shared writer
   described above, all rows from one file sharing that file's `ImportBatchId`.
5. Insert ordering within a batch: `quotes` → `stageDirections`/`soundCues` → `conversations` (FK
   dependency order) — `ConversationLines` rows can only be written once the rows they reference
   exist.
6. `SourceFiles_ConformToSchema` (`Quotinator.Core.Tests`) continues to pass for the migrated
   curated file.

---

## Design

### 1. Import DTOs

**Status:** ✅ Done

New `Quotinator.Core.Import` classes, `[JsonPropertyName]` on every property matching
`schemas/source-extended.schema.json`'s field names exactly: `SourceStageDirection`,
`SourceStageDirectionTranslation`, `SourceSoundCue`, `SourceSoundCueTranslation`,
`SourceConversation`, `SourceConversationLine`. `ParsedSourceFile` bundles all four sections
(`Quotes`/`StageDirections`/`SoundCues`/`Conversations`) as the return shape for the new parse
method in step 2.

`SourceConversationLine.Type` needed a shared enum discriminator (`"quote"` / `"stage_direction"` /
`"sound_cue"`). Originally added as `Quotinator.Engine.Entities.ConversationLineType` in #67
(mirroring `ImportBatchType`/`ImportBatchStatus`, which are Engine-only) — but unlike those two, this
type is needed by a `Quotinator.Core` import DTO too, and Core cannot depend on Engine. **Moved** to
`Quotinator.Core.Models.ConversationLineType`, mirroring exactly how `QuoteType` is defined once in
Core and reused by both `SourceQuote` (Core) and `Source`/`ConversationLineEntity` (Engine). New
`ConversationLineTypeJsonConverter : JsonStringEnumConverter<ConversationLineType>` using
`JsonNamingPolicy.SnakeCaseLower` (the schema's wire values use underscores, e.g.
`stage_direction` — `QuoteTypeJsonConverter`'s `KebabCaseLower` precedent doesn't match this wire
format, so a new converter was needed rather than reusing that one). `ConversationLineEntity.cs`
updated to `using Quotinator.Core.Models;` for the moved type; no other change to its shape.

### 2. Extend the file reader

**Status:** ✅ Done

New `SourceQuoteFileReader.TryParseExtended(json, out ParsedSourceFile? result)`, added alongside
the existing `TryParse` (kept unchanged — its callers, including the legacy `QuoteService` fixed
below, only need `Quotes`). Reuses the identical single `JsonNode.Parse` top-level shape sniff
`TryParse` already used — no second manual-walk site. A bare top-level array yields empty lists for
the three new sections, matching the schema's documented backward-compatibility rule.

**Found and fixed a real regression along the way:** `Quotinator.Core.Services.QuoteService` (the
legacy v1 flat-file in-memory service — confirmed via `Program.cs` that nothing registers it
anymore; only `QuoteServiceTests.cs` still exercises it) called
`JsonSerializer.Deserialize<List<SourceQuote>>(json, options)` directly, duplicating (and now
disagreeing with) `SourceQuoteFileReader`'s parsing logic. Converting `quotinator-curated.json` to
the extended object shape broke it. Fixed by having `QuoteService.Load` call
`SourceQuoteFileReader.TryParse` instead — one parsing implementation, not two, per the JSON parsing
policy; also a correctness fix in its own right, independent of this issue.

### 3. Staging-engine integration

**Status:** ✅ Done

Implemented as designed (below is what shipped, not a forward-looking plan). No standalone
`ConversationSeedWriter` class — the write logic lives directly in `ImportActionPlanner` (planning)
and `SqliteImportActionService` (apply/reverse), extending both in place rather than adding a
parallel writer, since (per the design) `Conversation`/`StageDirection`/`SoundCue` reuse the same
`EnsureXExistsAsync`/switch-dispatch shape those files already had for `Source`/`Character`/`Person`.

**Key simplification, confirmed while implementing:** `ConversationLines`, `StageDirectionTranslations`,
and `SoundCueTranslations` do **not** have their own top-level `SystemImportAction` rows — they are
detail rows of their parent (`Conversation`/`StageDirection`/`SoundCue`), the same relationship
`QuoteGenres`/`QuoteTranslations` have to `Quote`. This cuts the new `EntityType` surface from 6 down
to **3**: `Conversation`, `StageDirection`, `SoundCue` (`ImportActionEntityTypes.cs`).

**Identity model differs from Source/Character/Person:** those three use `EntityIdentity`-derived
stable ids (always uppercase). `StageDirection`, `SoundCue`, and `Conversation` carry an **explicit
`id` in the source file**, exactly like `Quote` — so Add-detection is a case-sensitive id lookup
(`Sql.Conversations.SelectIdById` etc.), and — like `Quote` — every place that writes or hard-deletes
them uses raw SQL (`RepositorySql.HardDelete`/`SoftDelete("Conversations")` etc.), never the
`IRestorableRepository<T>.HardDeleteAsync(Guid)`/`SoftDeleteAsync(Guid)` path, which forces uppercase
comparison and would silently no-op against a lowercase file-supplied id. All three are **Add-only**
— no `Modify`/merge semantics; re-importing the same ids is a no-op (already exists → skip).

**`ImportActionPlanner`** gained `PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/`PlanConversationsAsync`,
called from `PlanAsync` after the existing quote-planning loop, in that order (stage directions/sound
cues before conversations, since a conversation's lines reference them). `PlanAsync`'s signature
gained three new optional parameters (`stageDirections`, `soundCues`, `conversations`, all
defaulting to `[]`) rather than a breaking change to existing callers. New payload records:
`StageDirectionActionPayload`, `SoundCueActionPayload`, `ConversationLinePayload`,
`ConversationActionPayload` (the last carries the full ordered line list — lines are never staged as
separate actions).

**Correctness issue found and fixed while implementing (not anticipated at design time):**
`Sql.SystemImportActions.SelectAllForBatch` had no `ORDER BY`, so `TryApplyBatchAsync`'s per-action
apply callback had no guaranteed ordering — the design's claim that "stage directions/sound cues
apply before conversations because they're planned first" was false; SQLite doesn't promise scan
order without one. Fixed generically, in `Quotinator.Data` (not entity-type-aware): added
`ORDER BY rowid ASC`, relying on `WriteManyAsync`'s sequential single-row inserts to preserve
planning order. This is a real, domain-agnostic improvement (also makes `GetPagedAsync`'s and
reversal's action ordering deterministic), not a `Quotinator.Engine`-only workaround.

**`ApplyResolvedActionAsync`** gained three `case` arms. `StageDirection`/`SoundCue`:
`INSERT OR IGNORE` via `EnsureStageDirectionExistsAsync`/`EnsureSoundCueExistsAsync`, each inserting
its translation rows in a loop, then a `SystemChangeLog` entry. `Conversation`: `INSERT OR IGNORE`
the `Conversations` row, then loop the payload's `Lines` inserting each as a `ConversationLines` row
(exercising the #67 CHECK constraints for real), then a `SystemChangeLog` entry. Trusts its
referenced `Quote`/`StageDirection`/`SoundCue` rows already applied (per the ordering fix above) —
deliberately **not** defensive like `Character`'s Source-ensure, since — unlike a `Source`'s
title/type — a `Quote`'s full mergeable field set can't practically be denormalized into every
`Conversation` that references it.

**`ClearStaleAddTargetsAsync`** gained three loops (raw SQL, not repository calls — see the identity
model note above), each clearing the entity's own detail rows first via new `Sql.cs`
`DeleteForConversation`/`DeleteForStageDirection`/`DeleteForSoundCue` constants (mirroring
`QuoteGenres.DeleteForQuote`), then hard-deleting the stale row itself.

**`ReverseAppliedActionsAsync`**'s ordering dictionary gained `Conversation = 0` (same tier as
`Quote`) and `StageDirection`/`SoundCue = 4` (last tier) — `Conversation` must reverse before
`StageDirection`/`SoundCue` so their active-reference check doesn't still see the about-to-be-removed
conversation's lines as live. New `case` arms: `Conversation` unconditionally soft-deletes (nothing
references a Conversation, so no active-reference check — its `ConversationLines` are left orphaned,
same precedent as `QuoteGenres`/`QuoteTranslations` on a reversed Quote Add); `StageDirection`/
`SoundCue` check `HasActiveReferencesAsync` first, exactly like `Character`/`Source`/`Person`.

**Correctness issue found and fixed while implementing:** `Sql.StageDirections.CountActiveReferences`/
`Sql.SoundCues.CountActiveReferences` originally filtered `ConversationLines.IsDeleted = 0` directly
— but a `ConversationLines` row is never independently soft-deleted (see the detail-row point above),
so its own `IsDeleted` flag never reflects whether its *parent* conversation is still live, only the
parent's own `IsDeleted` does. Without the fix, reversing a conversation would permanently orphan its
`StageDirection`/`SoundCue` rows as unreversible, since the (never-updated) `ConversationLines.IsDeleted = 0`
would forever look like an active reference. Fixed by joining through `Conversations` and checking
*its* `IsDeleted` instead — caught by `ReverseBatchAsync_ConversationAdd_SoftDeletesConversationAndOrphanedStageDirection`
going red before the fix, not assumed correct.

**Repositories:** registered `IRestorableRepository<ConversationEntity/StageDirectionEntity/SoundCueEntity>`
in `Program.cs`, mirroring `Source`/`Character`/`Person`'s existing registrations — required for
`SqliteImportActionService`'s constructor (now 10 dependencies, up from 7) even though, per the
identity-model note above, the actual hard-delete/soft-delete calls go through raw SQL, not these
repositories' own `Guid`-typed methods. This reverses #67's original "no repositories needed yet"
decision for these three entities specifically — correct at the time (nothing consumed them yet);
this section is what now does.

**`GetPagedAsync`'s `BuildFields`/`ToFieldMap`** gained cases for the three new payload types, so
`GET /api/v1/import/actions` displays them (`text`/`imageUrl`, `text`/`soundFileUrl`/`imageUrl`,
`description`/`lineCount` respectively). `ComputeAmbiguousFields`/`ComputeRelatedActionIdsAsync`
needed no change — both already return empty for any `EntityType != Quote`.

**`Sql.cs` additions**, all picked up automatically by `SqlQueryGuardTests.SqlConstant_PassesAggregateGuard`
(reflection-driven, no test change needed) — none are dynamic factory methods, so no
`AssembledQueryCases` entries were needed either: `Conversations.{SelectIdById,InsertIfNotExists,DeleteAll}`,
`StageDirections.{SelectIdById,InsertIfNotExists,CountActiveReferences,DeleteAll}`,
`SoundCues.{SelectIdById,InsertIfNotExists,CountActiveReferences,DeleteAll}`,
`ConversationLines.{Insert,DeleteForConversation,DeleteForStageDirection,DeleteForSoundCue,DeleteAll}`,
`StageDirectionTranslations.{Insert,DeleteForStageDirection,DeleteAll}`,
`SoundCueTranslations.{Insert,DeleteForSoundCue,DeleteAll}`. The two new `CountActiveReferences`
constants (aggregate `COUNT(*)`) were added to `AggregateQueries_MatchDocumentedInventory`'s
documented inventory. `TruncateDataAsync` (`Quotinator.Data`, used by reseed) gained `DeleteAll`
calls for all six new tables, in FK-safe child-before-parent order.

**Wired into both write paths**, per the original design goal: `QuotinatorDatabaseInitializer`'s
`LoadQuotesFromFile` now wraps a new `LoadSourceFileAsync` (full `ParsedSourceFile` parse via
`TryParseExtended`) so startup seeding plans the extended sections; `SqliteQuoteImportService.ImportAsync`
does the same for `POST /api/v1/import` (a converter's output is always quotes-only JSON, so this
naturally yields empty extended sections whenever a converter ran — no conditional needed).

### 4. Migrate `quotinator-curated.json`

**Status:** ✅ Done

Converted from a flat array to `{ "quotes": [...], "stageDirections": [...], "soundCues": [...],
"conversations": [...] }`. Four conversations — see Scope correction 2. Validated via
`TryParseExtended_CuratedFile_ParsesRealFourConversationsWithStageDirectionAndSoundCue`
(`Quotinator.Core.Tests`) and `SourceDataIntegrityTests.SourceFiles_ConformToSchema`.

The eight new quotes changed total counts across the bundled dataset. Updated the hardcoded
assertions this affected: `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_SeedsExpectedCounts`
(788→796 quotes, sources unchanged at 479 — the three new movies already existed as `Sources` rows
in the `vilaboim`/`NikhilNamal17` bundled datasets, characters 2→7) and
`InitialiseAsync_CuratedFileOnly_SeedsFkChainCorrectly` (2→10 quotes, 1→4 sources, 2→7 characters,
curated-only so the three new sources are genuinely new here).

### 5. Documentation

**Status:** ⬜ Not started

`docs/data-import.md` already describes `quotinator-curated.json` as using the "(extended format)"
— accurate now that step 4 has landed; no wording change needed there. Still open: `README.md`/
`addon/DOCS.md` don't yet mention that conversations are seedable/importable, and
`docs/data-import.md`'s format table doesn't note which entity types are Add-only (Conversation/
StageDirection/SoundCue) vs mergeable (Quote). Deferred — not required for #69 (API surface) to
proceed, and better bundled with #69's own doc updates (new endpoint, new `QuoteResponse` field)
than done twice.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Extended-format file parses all four sections correctly | Unit test | `SourceQuoteFileReaderTests.TryParseExtended_FullObject_ParsesAllFourSections` |
| 2 | ✅ | Flat array file still parses with empty conversation sections | Unit test | `SourceQuoteFileReaderTests.TryParseExtended_BareArray_YieldsQuotesAndEmptyExtendedSections` |
| 3 | ✅ | `quotinator-curated.json` conforms to `source-extended.schema.json` after migration, and its four conversations parse with the expected shape | Unit test | `SourceDataIntegrityTests.SourceFiles_ConformToSchema`; `SourceQuoteFileReaderTests.TryParseExtended_CuratedFile_ParsesRealFourConversationsWithStageDirectionAndSoundCue` |
| 4 | ✅ | Seeding a file with all four sections writes rows to `Conversations`/`ConversationLines`/`StageDirections`/`StageDirectionTranslations`/`SoundCues`/`SoundCueTranslations` sharing one `ImportBatchId` | Unit test | `DatabaseInitializerTests.InitialiseAsync_CuratedFileOnly_SeedsConversationsStageDirectionsAndSoundCues` |
| 5 | ✅ | Re-seeding/re-importing the same file does not duplicate a reused `StageDirection`/`SoundCue`/`Conversation` (id already exists → skip) | Unit test | `DatabaseInitializerTests.ReseedAsync_CuratedFileOnly_ReproducesSameConversationCountsNotDoubled`; `QuoteImportServiceTests.ImportAsync_SameExtendedFormatFileImportedTwice_DoesNotDuplicateConversationOrStageDirection` |
| 6 | ✅ | `Conversation`/`StageDirection`/`SoundCue` Add actions are staged through `System_ImportActions` like `Quote`'s | Unit test | `DatabaseInitializerTests.InitialiseAsync_CuratedFileOnly_SeedsConversationsStageDirectionsAndSoundCues` (asserts the three `EntityType` values are present); `QuoteImportServiceTests.ImportAsync_ExtendedFormatFile_StagesAndAppliesConversationAndStageDirection` |
| 7 | ✅ | An applied batch containing conversations can be reversed (#59) — `Conversation` before `StageDirection`/`SoundCue`, referenced-row protection honoured | Unit test | `SqliteImportActionServiceTests.ReverseBatchAsync_ConversationAdd_SoftDeletesConversationAndOrphanedStageDirection`; `.ReverseBatchAsync_StageDirectionStillReferencedByAnotherConversation_IsKeptNotSoftDeleted` |
| 8 | ✅ | `POST /api/v1/import` can import a file containing conversations | Live (T2) | `curl -X POST -H "X-Api-Key: ..." -F "file=@data/sources/quotinator-curated.json" .../api/v1/import` against the built container — `200`, all 4 conversations visible via `GET /import/actions?entityType=Conversation&status=Applied`; re-running the same `curl` a second time still shows exactly the same 4 (same ids, same original `batchId`), confirming live re-import idempotency |
| 9 | ✅ | App starts and seeds the migrated curated file without error, including the new conversation tables | Live (T1) | Confirmed by the developer running the solution in Visual Studio (Debug build): starts cleanly, `schema is up to date (data v9, app v8)`, serves `/api/v1/quotes/random` successfully. The actual fresh-seed content (`seeding complete — 796 unique quotes`, `Conversations=4, ConversationLines=13, StageDirections=2, SoundCues=1`) was confirmed separately via `docker run` against a fresh container (T2, row 10) — the dev database used for the Visual Studio run above already existed from prior sessions, so it took the incremental-migration path rather than a fresh seed. |
| 10 | ✅ | Docker build succeeds with the migrated curated file and full seeding | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; `docker run` + `/api/v1/version` returned `"quotes":796,"sources":479,"characters":7`; no errors in container logs |

Full solution: `dotnet build --configuration Release` and `dotnet test --configuration Release` both
clean — 0 warnings, 0 errors, every project green (Engine.Tests: 131/131 after these additions).

---

## Notes

The Airplane! conversation is the canonical first entry — two quotes already in
`quotinator-curated.json` ("Surely you can't be serious." / "I am serious. And don't call me
Shirley."), linked in order, no stage directions or sound cues required.
