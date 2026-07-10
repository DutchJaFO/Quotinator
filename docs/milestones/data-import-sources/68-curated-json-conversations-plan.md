# #68 — Curated JSON: conversations format

**Status:** Planning
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

---

## Spec requirements (as corrected)

1. Source files support the extended object format already defined in
   `schemas/source-extended.schema.json`: `{ "quotes": [...], "stageDirections": [...],
   "soundCues": [...], "conversations": [...] }`. The flat top-level array format remains valid
   (treated as `{ "quotes": [...] }`).
2. `quotinator-curated.json` migrated from a flat array to the extended object format, with the
   Airplane! conversation added as the first real entry — the two Ted Striker / Dr. Rumack quotes
   already present, linked by one `conversations` entry.
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

**Status:** ⬜ Not started

New `Quotinator.Core.Import` classes mirroring `SourceQuote`'s shape (`[JsonPropertyName]` on every
property, matching `schemas/source-extended.schema.json`'s field names exactly):
`SourceStageDirection`, `SourceSoundCue`, `SourceConversation`, `SourceConversationLine` (with a
`Type` discriminator — `"quote"` / `"stage_direction"` / `"sound_cue"` — and the corresponding
`QuoteId`/`StageDirectionId`/`SoundCueId`), plus `SourceStageDirectionTranslation` and
`SourceSoundCueTranslation` (both just `{ Text }`, unlike `SourceQuoteTranslation` which also
carries `Source`).

### 2. Extend the file reader

**Status:** ⬜ Not started

`SourceQuoteFileReader.TryParse` currently discards everything except `quotes` from the wrapper
object. Extend it (or add a sibling `SourceFileReader.TryParse` returning all four sections) to also
deserialize `stageDirections`, `soundCues`, and `conversations` via `JsonSerializer.Deserialize`,
reusing the same single `JsonNode.Parse` call for the top-level shape sniff — never adding a second
manual-walk site. A bare top-level array still yields empty lists for the three new sections.

### 3. `ConversationSeedWriter`

**Status:** ⬜ Not started

New `Quotinator.Engine.Database.ConversationSeedWriter`, structured like `QuoteSeedWriter`: static
insert/merge primitives taking a connection, transaction, and `ChangeLogContext`, called from both
`QuotinatorDatabaseInitializer` and the live import service. Each write goes through
`IImportActionResolutionCoordinator`/`System_ImportActions` staging (mirroring how #154 staged
`Quote` writes) with `EntityType` set to `"Conversation"`, `"StageDirection"`, or `"SoundCue"`.
`StageDirections`/`SoundCues` are looked up/created by natural key (their `Text`, scoped per source
file) so re-seeding the same file doesn't duplicate a reused stage direction or sound cue —
mirroring `QuoteSeedWriter.GetOrCreateSourceAsync`'s cache-then-database-check pattern.

### 4. Migrate `quotinator-curated.json`

**Status:** ⬜ Not started

Convert the file's top-level shape from a bare array to `{ "quotes": [...] }`, then add:
- `conversations`: one entry linking the two existing Airplane! quotes in order.
- No `stageDirections`/`soundCues` needed for this first entry (the spec's own scope note).

### 5. Documentation

**Status:** ⬜ Not started

`docs/data-import.md` already describes `quotinator-curated.json` as using the "(extended format)"
— currently inaccurate (the file is still a flat array) until this issue ships; no change needed to
that doc's wording, just confirm it becomes true once step 4 lands.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Extended-format file parses all four sections correctly | Unit test | New test in `Quotinator.Core.Tests` for the extended file reader |
| 2 | ⬜ | Flat array file still parses with empty conversation sections | Unit test | New test asserting `stageDirections`/`soundCues`/`conversations` are empty for a bare-array input |
| 3 | ⬜ | `quotinator-curated.json` conforms to `source-extended.schema.json` after migration | Unit test | `SourceDataIntegrityTests.SourceFiles_ConformToSchema` |
| 4 | ⬜ | Seeding a file with all four sections writes rows to all six tables sharing one `ImportBatchId` | Unit test | New `Quotinator.Engine.Tests` integration test against a real SQLite DB |
| 5 | ⬜ | Re-seeding the same file does not duplicate a reused `StageDirection`/`SoundCue` | Unit test | New test: seed twice, assert row count unchanged |
| 6 | ⬜ | `ConversationSeedWriter` writes are staged through `System_ImportActions` like `QuoteSeedWriter`'s | Unit test | New test asserting `System_ImportActions` rows exist with `EntityType` = `Conversation`/`StageDirection`/`SoundCue` |
| 7 | ⬜ | `POST /api/v1/import` can import a file containing conversations | Live (T1) | `curl -F "file=@data/sources/quotinator-curated.json" .../api/v1/import` after step 4 lands; confirm `200`/`202` |
| 8 | ⬜ | App starts and seeds the migrated curated file without error | Live (T1) | Run `dotnet run --project src/Quotinator.Api`; confirm no startup exception |
| 9 | ⬜ | Docker build succeeds with the migrated curated file | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` |

---

## Notes

The Airplane! conversation is the canonical first entry — two quotes already in
`quotinator-curated.json` ("Surely you can't be serious." / "I am serious. And don't call me
Shirley."), linked in order, no stage directions or sound cues required.
