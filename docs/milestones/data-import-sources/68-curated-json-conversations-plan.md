# #68 — Curated JSON: conversations format

**Status:** In progress (step 3)
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

**Scope correction 3 (mid-implementation, confirmed with the user):** Section 3 below
(`ConversationSeedWriter`) turned out to be substantially larger than originally estimated once the
actual apply/reverse pipeline (`SqliteImportActionService.cs`, ~650 lines) was read in full — it's a
hand-rolled per-entity-type state machine (planning, dependency ordering, decide, apply, reverse),
not a simple writer function. The user chose to keep the original "full staging engine integration"
design rather than descope to a simpler direct-write path. Section 3 now carries a concrete,
implementation-ready design (informed by reading `ImportActionPlanner.cs` and
`SqliteImportActionService.cs` end to end) rather than the vague one-paragraph sketch this section
originally had — but the code itself is **not yet written**; this was deliberately left for a
follow-up session rather than rushed. See the header's `In progress (step 3)` status.

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

### 3. `ConversationSeedWriter` — design (code not yet written)

**Status:** ⬜ Not started — full design below, ready to implement in a follow-up session

Read `ImportActionPlanner.cs` and `SqliteImportActionService.cs` in full before writing this design
— the actual apply/reverse pipeline is a ~650-line, per-`EntityType` state machine (planning,
dependency ordering, decide, apply, reverse), not a simple insert function. The design below is
scoped to reuse as much of that existing machinery as possible.

**Key simplification found while designing:** `ConversationLines`, `StageDirectionTranslations`, and
`SoundCueTranslations` do **not** need their own top-level `SystemImportAction` rows. They are detail
rows of their parent (`Conversation`/`StageDirection`/`SoundCue`) — the same relationship
`QuoteGenres`/`QuoteTranslations` already have to `Quote` (written alongside the parent's own apply
step via `QuoteSeedWriter.InsertGenresAsync`, never staged as their own actions). This cuts the new
`EntityType` surface from 6 down to **3**: `Conversation`, `StageDirection`, `SoundCue`.

**Identity model differs from Source/Character/Person:** those three use `EntityIdentity`-derived
stable ids (existence check by natural key — title/name — since the file never supplies an id for
them). `StageDirection`, `SoundCue`, and `Conversation` all carry an **explicit `id` in the source
file**, exactly like `Quote` does. So their Add-detection is an id lookup (does a row with this exact
id already exist?), not a natural-key lookup. All three are **Add-only** — no `Modify`/merge
semantics, matching `Source`/`Character`/`Person` (never revised once created) rather than `Quote`
(which has full field-merge conflict resolution). Re-importing the same file with the same ids is a
no-op (id already exists → skip), which is sufficient today; nothing requires editable stage
directions/sound cues yet.

**`ImportActionPlanner.PlanAsync` extension** (or a sibling `ConversationActionPlanner`, called after
the existing quote-planning loop, sharing the same `batchId`/`policy`/`now`):
1. For each `SourceStageDirection`: id-lookup against `StageDirections`; if absent, stage an `Add`
   action (`EntityType = StageDirection`, payload = `{ Text, ImageUrl, Translations }`).
2. For each `SourceSoundCue`: same pattern (`EntityType = SoundCue`, payload = `{ Text,
   SoundFileUrl, ImageUrl, Translations }`).
3. For each `SourceConversation`: id-lookup against `Conversations`; if absent, stage an `Add`
   action (`EntityType = Conversation`, payload = `{ Description, Lines: [{ Order, Type, QuoteId,
   StageDirectionId, SoundCueId }] }` — the full line list travels in the Conversation's own
   payload, not as separate actions). No FK-existence validation needed at plan time — every
   referenced `QuoteId`/`StageDirectionId`/`SoundCueId` in the curated file is defined in the same
   file and staged in the same batch; a line referencing an id from a *different* file is out of
   scope until a real cross-file use case exists (flag as a known gap, not solved here).

**`ApplyResolvedActionAsync` extension** (new `case` arms in the existing `switch
(action.EntityType)`):
- `StageDirection` / `SoundCue`: `INSERT OR IGNORE` (idempotent, matching
  `EnsureSourceExistsAsync`'s shape) plus their translation rows (raw SQL loop, matching
  `QuoteSeedWriter.InsertGenresAsync`'s pattern — new `Sql.StageDirectionTranslations.Insert` /
  `Sql.SoundCueTranslations.Insert` constants needed), then a `SystemChangeLog` entry
  (`ChangeAction.Created`).
- `Conversation`: `INSERT OR IGNORE` the `Conversations` row, then loop its payload's `Lines` and
  insert each as a `ConversationLines` row (new `Sql.ConversationLines.Insert` constant) — this is
  where the CHECK constraint from #67 gets exercised for real. Then a `SystemChangeLog` entry.

**Ordering:** `ClearStaleAddTargetsAsync` (the #59 stale-row hard-delete pass) needs `Conversation`
handled **before** `StageDirection`/`SoundCue` (children before parents — a stale `ConversationLines`
row referencing a stale `StageDirection` would otherwise FK-violate on the `StageDirection`'s
hard-delete, same reasoning already documented for Quote-before-Character-before-Source). The
`ReverseAppliedActionsAsync` ordering dictionary needs the same relative order
(`Conversation = 0`-ish tier, `StageDirection`/`SoundCue` a later tier) — copy the existing
Quote/Character/Source pattern, don't invent a new scheme.

**Repositories:** `Conversations`, `StageDirections`, `SoundCues` need
`IRestorableRepository<T>` registrations in `Program.cs` (mirroring `Source`/`Character`/`Person` —
required for `ClearStaleAddTargetsAsync`'s hard-delete and `ReverseAppliedActionsAsync`'s
soft-delete/restore). `ConversationLines`/`StageDirectionTranslations`/`SoundCueTranslations` do
**not** get repositories — deleted/reinserted via raw SQL alongside their parent, matching
`QuoteGenres`/`QuoteTranslations`. This reverses #67's original "no repositories needed yet"
decision for the three parent entities specifically — that decision was correct at the time (nothing
consumed them yet); this section is what now consumes them.

**`GetPagedAsync`'s `BuildFields`/`ToFieldMap`** need new cases for `StageDirection`/`SoundCue`/
`Conversation` payloads (mirroring `SourceActionPayload`'s `ToFieldMap`) so `GET
/api/v1/import/actions` displays them sensibly. `ComputeAmbiguousFields`/`ComputeRelatedActionIdsAsync`
need **no change** — both already return `[]`/empty for any `EntityType != Quote`, which is exactly
right for three more Add-only entity types.

**New `Sql.cs` additions** (each needs a `SqlQueryGuardTests.AssembledQueryCases` entry per the SQL
centralisation policy): `Conversations.InsertIfNotExists`, `StageDirections.InsertIfNotExists`,
`SoundCues.InsertIfNotExists`, `ConversationLines.Insert`, `StageDirectionTranslations.Insert`,
`SoundCueTranslations.Insert`, plus `CountActiveReferences`-style queries for each of the three
parent entities (needed by `ReverseAppliedActionsAsync`'s "still referenced, don't soft-delete"
check — `StageDirection`/`SoundCue` are referenced by `ConversationLines`, mirroring how
`Sql.Characters.CountActiveReferences` checks `Quotes`).

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
— was inaccurate (the file was still a flat array) until step 4 above landed; now true. No wording
change needed. Revisit once step 3 ships: `README.md`/`addon/DOCS.md` may need a line noting
conversations are seedable, and `docs/data-import.md`'s format table could note which entity types
are Add-only vs mergeable.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Extended-format file parses all four sections correctly | Unit test | `SourceQuoteFileReaderTests.TryParseExtended_FullObject_ParsesAllFourSections` |
| 2 | ✅ | Flat array file still parses with empty conversation sections | Unit test | `SourceQuoteFileReaderTests.TryParseExtended_BareArray_YieldsQuotesAndEmptyExtendedSections` |
| 3 | ✅ | `quotinator-curated.json` conforms to `source-extended.schema.json` after migration, and its four conversations parse with the expected shape | Unit test | `SourceDataIntegrityTests.SourceFiles_ConformToSchema`; `SourceQuoteFileReaderTests.TryParseExtended_CuratedFile_ParsesRealFourConversationsWithStageDirectionAndSoundCue` |
| 4 | ⬜ | Seeding a file with all four sections writes rows to `Conversations`/`ConversationLines`/`StageDirections`/`StageDirectionTranslations`/`SoundCues`/`SoundCueTranslations` sharing one `ImportBatchId` | Unit test | New `Quotinator.Engine.Tests` integration test against a real SQLite DB (blocked on step 3) |
| 5 | ⬜ | Re-seeding the same file does not duplicate a reused `StageDirection`/`SoundCue`/`Conversation` (id already exists → skip) | Unit test | New test: seed twice, assert row count unchanged (blocked on step 3) |
| 6 | ⬜ | `Conversation`/`StageDirection`/`SoundCue` Add actions are staged through `System_ImportActions` like `Quote`'s | Unit test | New test asserting `System_ImportActions` rows exist with the new `EntityType` values (blocked on step 3) |
| 7 | ⬜ | An applied batch containing conversations can be reversed (#59) — `Conversation` before `StageDirection`/`SoundCue`, referenced-row protection honoured | Unit test | New test mirroring `ReverseBatchAsync_QuoteAdd_SoftDeletesOrphanedSourceAndCharacter` (blocked on step 3) |
| 8 | ⬜ | `POST /api/v1/import` can import a file containing conversations | Live (T1) | `curl -F "file=@data/sources/quotinator-curated.json" .../api/v1/import` (blocked on step 3) |
| 9 | ⬜ | App starts and seeds the migrated curated file without error, including the new conversation tables | Live (T1) | Run `dotnet run --project src/Quotinator.Api`; confirm no startup exception (blocked on step 3) |
| 10 | ⬜ | Docker build succeeds with the migrated curated file and full seeding | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` (blocked on step 3) |

---

## Notes

The Airplane! conversation is the canonical first entry — two quotes already in
`quotinator-curated.json` ("Surely you can't be serious." / "I am serious. And don't call me
Shirley."), linked in order, no stage directions or sound cues required.
