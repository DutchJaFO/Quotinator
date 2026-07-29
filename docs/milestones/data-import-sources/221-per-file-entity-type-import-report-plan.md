# #221 — Per-file, per-entity-type import/seed report

**Status:** Released

**GitHub issue:** https://github.com/DutchJaFO/Quotinator/issues/221

**Depends on:** none

**Tiers required:** T1, T2

---

## Summary

The startup log and the `/admin/database/reseed`/`reset` endpoint responses report a single flat
`duplicates` count, with no indication of which file it came from, whether it was a genuine
correction or a new row, or anything about non-Quote entity types. `System_ImportActions` already
carries everything needed to answer all of that — this issue replaces the flat count with a
per-file, per-entity-type breakdown built directly from the actions each file's own planning pass
already produces, and wires it into every place a seed/import operation reports back.

## Design decisions (confirmed with developer, 2026-07-26)

1. **`IDatabaseInitializer.LastSeedDuplicates`/`SeedDuplicateRecord` are replaced entirely** by the
   new report — not kept alongside it. `SeedDuplicateRecord` the type is removed once nothing
   references it (see Decision 3 below for the one other place it lives).
2. **The report breaks out by all 6 real `ImportActionStatus` values, no collapsing** — `new`
   (Add, resolved), `modified` (Modify, resolved), `blocked`, `discarded`, `pending`, `stale`. Every
   action falls into exactly one of these six buckets, keyed by `(ActionType, Status)`:
   - `Add` + (`Decided` or `Applied`) → `new`
   - `Modify` + (`Decided` or `Applied`) → `modified`
   - any `ActionType` + `Blocked` → `blocked`
   - any `ActionType` + `Discarded` → `discarded`
   - any `ActionType` + `Pending` → `pending`
   - any `ActionType` + `Stale` → `stale`

   This is the issue's own draft shape, unchanged, with `stale` added as the 6th bucket for #153
   awareness (the issue was filed before #153 added that status).
3. **`POST /import`'s single-file report uses the same per-file wrapper shape as the multi-file
   reseed/reset report** — one consistent contract, even though `POST /import` always has exactly
   one file.
4. **`GET /admin/database/seed/preview` also migrates to the new report shape** — a bigger change
   than the issue's own "where this must surface" list names, confirmed explicitly with the
   developer. This endpoint currently computes `CrossFileDuplicates` via a simple in-memory quote-id
   collision scan with **no database access at all** (see its own doc comment: "never touches the
   database"). Producing the same rich per-entity-type/per-status report requires the real
   `ImportActionPlanner.PlanAsync` (Source/Character/Person resolution, rule-file consultation, the
   full Add/Modify/Blocked/Pending/Stale classification) — but `PlanAsync` itself is already
   side-effect-free (confirmed by reading it: every database call in the file is `ExecuteScalarAsync`
   — a `SELECT` — never an `INSERT`/`UPDATE`/`DELETE`). The fix calls `PlanAsync` per file and builds
   the report from its returned actions directly, **never calling `IImportActionCoordinator.StageAsync`
   or `IImportActionService.ApplyBatchAsync`** — preserving the "never touches the database" contract
   (no `ImportBatch`/`SystemImportAction` rows are written) while getting the full rich report. This
   is a different (stronger) guarantee than `POST /import/preview`'s own "stage but don't apply"
   pattern, which does write real, inspectable rows — `GET /admin/database/seed/preview` writes
   nothing at all, matching its existing, documented behaviour.

## Related, in-scope gap: missing entity-type counts in startup stats

`IDatabaseInitializer`'s `QuoteCount`/`SourceCount`/`CharacterCount`/`PeopleCount` and the
`[Database - Stats]` log line predate `Series`, `Universe`, `StageDirection`, `SoundCue`, and
`Conversation`. This issue adds count properties and query support for all five, per the issue
body's own "Related gap" section.

---

### 1. `ImportActionReport` — the shared report DTO and builder

**Status:** Done, implemented 2026-07-26.

`EntityTypeActionCounts`/`FileImportReport` (`Quotinator.Core.Models`) and
`ImportActionReportBuilder.Build` (`Quotinator.Core.Database`, internal) implemented exactly as
designed above — 13 unit tests in `ImportActionReportBuilderTests.cs`, all passing.

New types in `Quotinator.Core.Models` (response-shape DTOs, matching `MasterDataReference`'s own
precedent for where these live):

```csharp
public sealed class EntityTypeActionCounts
{
    public required int New { get; init; }
    public required int Modified { get; init; }
    public required int Blocked { get; init; }
    public required int Discarded { get; init; }
    public required int Pending { get; init; }
    public required int Stale { get; init; }
}

public sealed class FileImportReport
{
    public required string FileName { get; init; }
    public required IReadOnlyDictionary<string, EntityTypeActionCounts> EntityTypes { get; init; }
}
```

New builder `Quotinator.Core.Database.ImportActionReportBuilder` (static, pure function):

```csharp
internal static FileImportReport Build(string fileName, IReadOnlyList<SystemImportAction> actions)
```

Groups `actions` by `EntityType`, classifies each into one of the 6 buckets per Design Decision 2
above, returns one `FileImportReport`. Entity types with zero actions are omitted from the
dictionary (an empty `sources` entry for a file with no Source actions adds no information).

**Unit tests** (`ImportActionReportBuilderTests.cs`, new file):
- Empty actions list → empty `EntityTypes` dictionary
- One `Add`+`Decided` Quote action → `quotes: { new: 1, ... 0 everywhere else }`
- One `Add`+`Applied` Quote action → same as above (both `Decided` and `Applied` count as `new`)
- One `Modify`+`Applied` Quote action → `modified: 1`
- One `Add`+`Stale` action (a stale source-alias substitution, per #153) → `stale: 1`, not `new`
- One `Modify`+`Blocked`/`Discarded`/`Pending` action each → respective bucket, one test per status
- Actions across multiple entity types → each type gets its own independent counts
- Multiple actions of the same `(EntityType, bucket)` → counts accumulate correctly

### 2. Wire into the seeding pipeline — replace `LastSeedDuplicates`

**Status:** Done, implemented 2026-07-26.

`IDatabaseInitializer.LastSeedDuplicates`/`DatabaseInitializer.LastSeedDuplicates` replaced with
`LastSeedReport` (`IReadOnlyList<FileImportReport>`). `QuotinatorDatabaseInitializer
.SeedIfEmptyInternalAsync`'s per-file loop builds one `FileImportReport` right after `PlanAsync`
returns (before `StageAsync`/`ApplyBatchAsync`), replacing the old `lastFileByQuoteId`/`duplicates`/
`SeedDuplicateRecord`-based tracking entirely. The old aggregate "seeding complete — N unique quotes
from M total (D duplicates)" log line is replaced by one per-file `[Database - Seed] {File} report:
{...}` line (emitted right after that file's report is built) plus a short "seeding complete — N
file(s) processed" summary line. `NoOpDatabaseInitializer` and the two test-double
`IDatabaseInitializer` implementations (`AdminEndpointsTests.SpyDatabaseInitializer`,
`StartupSummaryLoggerTests.StubDbInitializer`) updated to match.

**ADR 004 correction made during this step**: `FileImportReport`/`EntityTypeActionCounts`/
`ImportActionReportBuilder` were initially placed in `Quotinator.Core` (Step 1), but since
`IDatabaseInitializer.LastSeedReport` lives in `Quotinator.Data` and only ever touches
`SystemImportAction` (already a `Quotinator.Data` entity), that would have required `Quotinator.Data`
to depend on `Quotinator.Core` — forbidden by ADR 004. Moved to `Quotinator.Data.Import` (and
`ImportActionReportBuilder` made `public`, since `Quotinator.Core` now calls it across the assembly
boundary) before Step 2's wiring — see the separate `refactor [#221]` commit.

**`AdminEndpoints.cs`'s `POST /admin/database/reseed`/`reset` responses updated too** (part of Step 6,
done here since Step 2 already needed the field renamed): `duplicates` (a bare int) replaced with
`reports` (the new per-file shape). `GET /admin/database/seed/preview` is unaffected here — its own
migration is Step 3.

Existing tests updated: `InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates` now asserts the
summed `Modified` count across all files' `Quote` entries equals 45 (matching the old duplicate
count), plus asserts zero `Pending`/`Blocked` (NewestWins always resolves deterministically);
`InitialiseAsync_CuratedFileOnly_SeedsFkChainCorrectly`'s duplicate-count assertion now checks
`Modified` is `0` for a single-file seed.

### 3. Wire into `PreviewSeedAsync` — real planner, no writes

**Status:** Done, implemented 2026-07-28.

`PreviewSeedAsync` rewritten to open its own read-only connection (`CreateConnection()` +
`OpenAsync()`) and, per file, call `LoadSourceFileAsync` for the full multi-entity parse and
`ImportActionPlanner.PlanAsync` (passing `transaction: null` and a fresh `Guid.NewGuid()` batch id,
since nothing is ever staged or committed) to build a `FileImportReport` via
`ImportActionReportBuilder.Build` — exactly the same classifier the real seeding pipeline uses.
`SeedPreviewResult` now carries `IReadOnlyList<FileImportReport> Reports` instead of
`CrossFileDuplicates`/`TotalQuotes`/`UniqueQuotes` (file quote counts are unaffected — they still live
on `SeedFilePreview`). `SeedDuplicateRecord.cs` deleted (no remaining references). The now-unused
`TruncateLabel` helper (only ever used by the old duplicate-tracking code) removed alongside it.

**Known limitation, documented on `SeedPreviewResult.Reports`'s own XML doc comment** (per Design
Decision 4 and the developer's explicit choice to accept it rather than build a full transactional
simulation): since nothing is actually written between files, a quote id appearing in two different
files that are both new to the database reports as `new` in both files' reports, not `new` in one and
`modified` in the other, unlike a real seed run (where the earlier file's row is actually committed
before the next file is planned). Always accurate against a database that already has the relevant
rows — confirmed by the new test below, which previews against an already-fully-seeded database and
gets the correct `modified` counts throughout.

**Test** (`DatabaseInitializerTests.PreviewSeedAsync_AfterFullSeed_ProducesReportWithoutWritingAnyRow`,
new): seeds `AllFilesBatch()` fully, records `System_ImportActions`/`ImportBatches` row counts, calls
`PreviewSeedAsync()`, and asserts both row counts are byte-for-byte unchanged and the returned report
shows `modified = 844` (799 unique quotes + 45 cross-file duplicate occurrences — every quote line
across every file matches an already-existing row) and `new = 0`.

### 4. Wire into `POST /import`/`POST /import/preview`

**Status:** Done, implemented 2026-07-28.

`ImportResultResponse` gains a `required FileImportReport Report` property (`Quotinator.Core.Models`,
depends on `Quotinator.Data.Import` — no ADR 004 concern, since Core is already allowed to depend on
Data). `SqliteQuoteImportService.ImportAsync` builds it from its own `actions` list (the same list
`ImportActionPlanner.PlanAsync` already returns) via `ImportActionReportBuilder.Build(fileName,
actions)`. `ApplyStagedBatchAsync` (the `batchId`-mode alias — applies an already-staged batch with no
fresh `PlanAsync` call) builds it from the actions it re-reads via `_actionReader.GetAllForBatchAsync`,
labelled with the batch's own stored `Name` (the original upload's file name) rather than a fresh
value, since there's no new file in this call. `ImportSummary`'s existing
`Total`/`Imported`/`Updated`/`Skipped`/`Errors` fields stay — they answer a different question
(row-level outcome including validation errors, which `FileImportReport` has no concept of) and
nothing here duplicates them. `ImportEndpoints.cs` needed no changes — both endpoints already return
the whole `ImportResultResponse` object directly (`Results.Ok(result)`), so `Report` reaches the wire
automatically.

**Tests** (`QuoteImportServiceTests.cs`, new): `ImportAsync_FreshDatabase_ReportShowsOneNewQuoteAction`
(a brand-new quote produces `Report.EntityTypes["Quote"].New == 1`) and
`ApplyStagedBatchAsync_PreviouslyStagedBatch_ReportShowsOneNewQuoteAction` (same, applied via the
`batchId` alias path, confirming the re-read-actions code path also builds a correct report). Four
existing `ImportResultResponse` object-initializer call sites in `ImportEndpointTests.cs` and
`FakeQuoteImportService.cs` updated to supply the now-required `Report` field.

### 5. Missing entity-type counts (Series/Universe/StageDirection/SoundCue/Conversation)

**Status:** Done, implemented 2026-07-28.

Added `SeriesCount`/`UniverseCount`/`StageDirectionCount`/`SoundCueCount`/`ConversationCount` to
`IDatabaseInitializer`/`DatabaseInitializer` (`protected set` properties, same shape as the existing
four), populated in `QuotinatorDatabaseInitializer.LogDatabaseStatsAsync` via three newly-added
`Sql.Conversations/StageDirections/SoundCues.CountActive` constants (`Series.CountActive` and
`Universe.CountActive` already existed from #180). The `[Database - Stats]` log line now reports all
nine counts. `NoOpDatabaseInitializer`, `AdminEndpointsTests.SpyDatabaseInitializer`, and
`StartupSummaryLoggerTests.StubDbInitializer` updated with matching zero-valued properties.
`POST /admin/database/reseed`/`reset` responses gain `series`/`universes`/`stageDirections`/
`soundCues`/`conversations` fields alongside the existing four.

**Test** (`DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_PopulatesNewEntityTypeCounts`, new):
seeds `AllFilesBatch()`, cross-checks each of the five new counts against a direct SQL `COUNT(*)`
query rather than a hardcoded literal (the exact bundled totals are incidental to what this test
proves). `SeriesCount`/`UniverseCount` are asserted as `0` — `AllFilesBatch()` (curated/vilaboim/
NikhilNamal17) deliberately excludes the separate `quotinator-series-universe.json` bundled file, so
this is the correct expected value, not an oversight.

**Regression found during this step, resolved by deleting both tests (developer's call, not a
workaround)**: `LogDatabaseStatsAsync` is called unconditionally after `OnInitialisedAsync`, and two
pre-existing migration-replay tests
(`InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally`,
`Migration_SeriesUniverseSchema_PopulatesCharacterSources1to1FromExistingSourceId`) broke because they
stop migration application at an intermediate checkpoint (before `Conversations`/`StageDirections`/
`SoundCues`/`Series`/`Universe` tables exist) before calling into that hook. Investigated an
`ApplyMigrationsForTestingAsync` test-only entry point (migrations only, skipping `OnInitialisedAsync`)
as a fix, but the developer identified both tests as structurally flawed independent of this
regression and rejected preserving them via a workaround: `InitialiseAsync_ExistingDatabaseAtVersion3_
...` pins its behaviour to an arbitrary, hardcoded migration checkpoint ("version 3") that has no
real-world meaning of its own; `Migration_SeriesUniverseSchema_PopulatesCharacterSources1to1FromExistin
gSourceId` hand-writes raw SQL `INSERT` statements against `Sources`/`Characters` instead of going
through the repository pattern this project otherwise uses everywhere, meaning it silently
re-duplicates entity shape knowledge that will need hand-fixing every time either entity's schema
changes. Both tests deleted outright; the `ApplyMigrationsForTestingAsync` addition was reverted since
nothing calls it. `Migration_SeriesUniverseSchema_DropsCharactersSourceIdColumn` (a sibling test in the
same file) is unaffected — it calls the ordinary, full `InitialiseAsync()`, never a partial-migration
checkpoint.

**Also required**: three new `Sql.Conversations/StageDirections/SoundCues.CountActive` constants
added to the `AggregateQueries_MatchDocumentedInventory` guard test's documented inventory in
`SqlQueryGuardTests.cs` (reviewed — plain `COUNT(*)`, no `GROUP BY`/`HAVING`, same as every other
`CountActive` constant).

### 6. Update admin endpoint responses

**Status:** Done, implemented 2026-07-28.

- `POST /admin/database/reseed`, `POST /admin/database/reset` — ✅ `duplicates` (a bare int) replaced
  with `reports` (one per file), done as part of Step 2 (2026-07-26); ✅ `series`/`universes`/
  `stageDirections`/`soundCues`/`conversations` added alongside the existing four counts, done as part
  of Step 5. `AdminEndpointsTests`'s two stats-shape tests updated to assert all nine fields.
- `GET /admin/database/seed/preview` — ✅ `totalQuotes`/`uniqueQuotes`/`crossFileDuplicates` replaced
  with `reports` (one per file, the new shape); `[Description]` text updated to document the known
  cross-file-simulation limitation. `AdminEndpointsTests.PreviewSeed_Returns200WithPreviewShape`
  updated to assert `files`/`reports` instead of the removed fields.

### 7. Documentation

**Status:** Done, implemented 2026-07-28.

- `README.md`, `addon/DOCS.md` — ✅ the five affected endpoint rows (`/import`, `/import/preview`,
  `/admin/database/seed/preview`, `/admin/database/reseed`, `/admin/database/reset`) updated to
  mention `report`/`reports` and the nine entity-type row counts.
- `[Description]`/`WithDescription` text on the affected endpoints in `AdminEndpoints.cs` (done as
  part of Steps 3/6) and `ImportEndpoints.cs`'s shared `ImportDescription` constant (this step) — all
  now document the new `report`/`reports` field and, for the admin endpoints, the nine entity-type
  counts.
- `CLAUDE.md`'s living T2 smoke-test checklist — rather than editing the existing reseed/reset/import
  entries in place (none of them asserted the old `duplicates`/`crossFileDuplicates` shape by name, so
  there was nothing stale to correct), added a new dedicated "Per-file, per-entity-type import/seed
  report (#221)" subsection covering all four surfaces (`seed/preview`, `reseed`, `reset`,
  `import`/`import/preview`) plus the widened `[Database - Stats]` log line.
- `docs/logging.md` — no changes needed; `[Database - Stats]` already existed and this only widens
  its own line.

**T2 — live-verified 2026-07-28** against a fresh `quotinator:local` Docker build: `[Database - Stats]`
log line shows all nine counts; `GET /admin/database/seed/preview` returns `reports` (no
`totalQuotes`/`uniqueQuotes`/`crossFileDuplicates`); `POST /admin/database/reseed` and `.../reset` both
return all nine row counts plus `reports`; `POST /import` and `.../import/preview` both return a
singular `report` alongside `summary`/`conflicts`/`errors`. All four shapes matched exactly what's
documented in `CLAUDE.md`'s new smoke-test subsection.

### 8. Tests — overall

**Status:** Full test suite and T2 done, 2026-07-28; T1 pending developer action.

All four integration-test bullets originally planned here were actually satisfied incrementally as
each step landed, rather than as a separate batch at the end:
- `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates`/
  `PopulatesNewEntityTypeCounts` (Steps 2/5) — `LastSeedReport`'s shape and the five new count
  properties, both against a real multi-file seed.
- `DatabaseInitializerTests.PreviewSeedAsync_AfterFullSeed_ProducesReportWithoutWritingAnyRow` (Step 3)
  — `PreviewSeedAsync` produces the expected report and never writes a row.
- `QuoteImportServiceTests.ImportAsync_FreshDatabase_ReportShowsOneNewQuoteAction`/
  `ApplyStagedBatchAsync_PreviouslyStagedBatch_ReportShowsOneNewQuoteAction` (Step 4) — `POST /import`
  and the `batchId`-mode alias both include a correct `Report`.
- `AdminEndpointsTests` (Steps 3/5/6) — `reseed`/`reset`/`seed/preview` responses include `reports`
  (or the nine row counts) in the new shape.

**Full regression suite**: `dotnet build --configuration Release` — 0 warnings/0 errors. `dotnet test
--configuration Release` — all tests pass across every project (1341 in `Quotinator.Core.Tests`, up
from the pre-#221 baseline; no failures anywhere), confirmed 2026-07-28 after every step's changes.

**T2 — live-verified 2026-07-28** (see Step 7's own T2 note for the full detail): fresh
`quotinator:local` Docker build, all four report surfaces and the widened `[Database - Stats]` log
line checked against a running container and matched exactly.

**T1 — partial success, one fix landed as a result (2026-07-28).** The developer ran the app in Visual
Studio and confirmed it starts without error, but flagged that `StartupSummaryLogger`'s closing banner
still crammed all four original counts onto a single line (`schema v11 (data v13) - 799 quotes  461
sources  12 characters  3 people`) — out of scope for this issue's original plan, but a real usability
problem the developer's own T1 pass surfaced: cramming even more counts onto one line "is not going to
work when we add even more items in the future." Fixed by splitting the schema/migration line from the
counts entirely: the banner now has its own `Statistics:` section with one line per entity type (all
nine — quotes, sources, characters, people, series, universes, stage directions, sound cues,
conversations), matching the label/indent style the rest of the banner already uses. Live-verified via
a fresh `quotinator:local` container showing the new format exactly as designed. Two
`StartupSummaryLoggerTests` updated/added: `LogReady_BannerContainsDbStats` now also asserts the
`Statistics:` header; new `LogReady_BannerContainsNewEntityTypeStats_OnePerLine` asserts all five new
counts appear. T1 itself (app starts, pages render) is confirmed done.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ImportActionReportBuilder` classifies every `(ActionType, Status)` combination into exactly one of 6 buckets | Unit test | `ImportActionReportBuilderTests.*` (13 tests), implemented 2026-07-26 |
| 2 | ✅ | Seeding produces a per-file report replacing `LastSeedDuplicates`, for all entity types | Unit test | `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates` (updated), implemented 2026-07-26 |
| 3 | ✅ | `PreviewSeedAsync` produces the same rich report without writing any row to the database | Unit test | `DatabaseInitializerTests.PreviewSeedAsync_AfterFullSeed_ProducesReportWithoutWritingAnyRow`, implemented 2026-07-28 |
| 4 | ✅ | `POST /import`/`/import/preview` responses include a per-file report | Unit test | `QuoteImportServiceTests.ImportAsync_FreshDatabase_ReportShowsOneNewQuoteAction`/`ApplyStagedBatchAsync_PreviouslyStagedBatch_ReportShowsOneNewQuoteAction`, implemented 2026-07-28 |
| 5 | ✅ | `POST /admin/database/reseed`/`reset` responses include per-file reports instead of a flat `duplicates` count | Unit test + Live | `AdminEndpointsTests.ReseedDatabase_CorrectKey_Returns200WithStatsShape`/`ResetDatabase_CorrectKey_Returns200WithStatsShape` (implemented 2026-07-26, extended 2026-07-28 for the five new counts) + `docs/smoke-tests.md`, live-verified 2026-07-28 |
| 6 | ✅ | `GET /admin/database/seed/preview` response includes per-file reports | Unit test + Live | `AdminEndpointsTests.PreviewSeed_Returns200WithPreviewShape` (updated 2026-07-28); live `curl` against a fresh `quotinator:local` container returned `reports` with the expected per-file/per-entity-type shape (no `totalQuotes`/`uniqueQuotes`/`crossFileDuplicates`), verified 2026-07-28 |
| 7 | ✅ | Startup stats log and `IDatabaseInitializer` expose counts for all 9 entity types (adding Series/Universe/StageDirection/SoundCue/Conversation) | Unit test + Live | `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_PopulatesNewEntityTypeCounts`, implemented 2026-07-28; `docker logs <container> \| grep "\[Database - Stats\]"` against a fresh container showed all nine counts, verified 2026-07-28 |
| 8 | ✅ | `SeedDuplicateRecord`/`LastSeedDuplicates`/`CrossFileDuplicates` are fully removed, no remaining references | Live (review) | `grep -rn "SeedDuplicateRecord\|LastSeedDuplicates\|CrossFileDuplicates" src/ tests/` (excluding bin/obj) returns only a test *method name* describing the still-existing cross-file-duplicate-detection behaviour, not a reference to any removed type — confirmed 2026-07-28 |
| 9 | ✅ | Documentation (README/DOCS.md/CLAUDE.md smoke tests) reflects the new response shapes | Live (review) | `README.md`/`addon/DOCS.md` rows and `CLAUDE.md`'s new #221 smoke-test subsection updated and live-verified against real endpoint responses, 2026-07-28 |
