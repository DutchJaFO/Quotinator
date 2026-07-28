# #221 — Per-file, per-entity-type import/seed report

**Status:** In progress (step 5)

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

**Status:** Not started.

Add `SeriesCount`/`UniverseCount`/`StageDirectionCount`/`SoundCueCount`/`ConversationCount` to
`IDatabaseInitializer`, implemented in `DatabaseInitializer` the same way the existing four counts
are (a `CountAll`-style query per table, updated after `InitialiseAsync`/`ReseedAsync`/`ResetAsync`).
Extend `LogDatabaseStatsAsync`'s `[Database - Stats]` log line to include all nine counts.
`NoOpDatabaseInitializer` (test double) gets matching zero-valued properties.

### 6. Update admin endpoint responses

**Status:** Done for the report shape (2026-07-28); the five new entity-type counts (Step 5) still
need adding to `reseed`/`reset`'s responses once that step lands.

- `POST /admin/database/reseed`, `POST /admin/database/reset` — ✅ `duplicates` (a bare int) replaced
  with `reports` (one per file), done as part of Step 2 (2026-07-26).
- `GET /admin/database/seed/preview` — ✅ `totalQuotes`/`uniqueQuotes`/`crossFileDuplicates` replaced
  with `reports` (one per file, the new shape); `[Description]` text updated to document the known
  cross-file-simulation limitation. `AdminEndpointsTests.PreviewSeed_Returns200WithPreviewShape`
  updated to assert `files`/`reports` instead of the removed fields.

### 7. Documentation

**Status:** Not started.

- `README.md`, `addon/DOCS.md` — update the three admin endpoint rows' descriptions.
- `[Description]` attributes on the affected endpoints in `AdminEndpoints.cs` and `ImportEndpoints.cs`.
- `CLAUDE.md`'s living T2 smoke-test checklist — update every existing reseed/reset/import smoke-test
  command's expected-output description to match the new response shape; the checklist already
  exercises all of these endpoints extensively, so this is corrections to existing entries, not new
  ones.
- `docs/logging.md` — no new prefix needed; `[Database - Stats]` already exists and this only widens
  its own line.

### 8. Tests — overall

**Status:** Not started.

Beyond Step 1's builder unit tests: integration tests proving the report reaches each of the four
call sites correctly —
- `DatabaseInitializerTests` — a multi-file seed (`AllFilesBatch()`) produces a `LastSeedReport` with
  the expected per-file/per-entity-type shape; the five new count properties are correct after
  seeding.
- `QuotinatorDatabaseInitializer` preview test — `PreviewSeedAsync` against real bundled files
  produces the expected report **and never writes any row** (assert `System_ImportActions`/
  `ImportBatches` row counts are unchanged before/after the call).
- `SqliteImportActionServiceTests`/endpoint tests — `POST /import` and `/import/preview` responses
  include the new `Report` field with correct counts for a known fixture file.
- `AdminEndpointsTests` — `reseed`/`reset`/`seed/preview` responses include `reports` in the new
  shape.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ImportActionReportBuilder` classifies every `(ActionType, Status)` combination into exactly one of 6 buckets | Unit test | `ImportActionReportBuilderTests.*` (13 tests), implemented 2026-07-26 |
| 2 | ✅ | Seeding produces a per-file report replacing `LastSeedDuplicates`, for all entity types | Unit test | `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates` (updated), implemented 2026-07-26 |
| 3 | ✅ | `PreviewSeedAsync` produces the same rich report without writing any row to the database | Unit test | `DatabaseInitializerTests.PreviewSeedAsync_AfterFullSeed_ProducesReportWithoutWritingAnyRow`, implemented 2026-07-28 |
| 4 | ✅ | `POST /import`/`/import/preview` responses include a per-file report | Unit test | `QuoteImportServiceTests.ImportAsync_FreshDatabase_ReportShowsOneNewQuoteAction`/`ApplyStagedBatchAsync_PreviouslyStagedBatch_ReportShowsOneNewQuoteAction`, implemented 2026-07-28 |
| 5 | ❌ | `POST /admin/database/reseed`/`reset` responses include per-file reports instead of a flat `duplicates` count | Unit test + Live | `AdminEndpointsTests` (new cases) + CLAUDE.md smoke test |
| 6 | ✅ | `GET /admin/database/seed/preview` response includes per-file reports | Unit test + Live | `AdminEndpointsTests.PreviewSeed_Returns200WithPreviewShape` (updated 2026-07-28); Live T2 pending Step 8 |
| 7 | ❌ | Startup stats log and `IDatabaseInitializer` expose counts for all 9 entity types (adding Series/Universe/StageDirection/SoundCue/Conversation) | Unit test + Live | `DatabaseInitializerTests` (new case) + live container log inspection |
| 8 | ❌ | `SeedDuplicateRecord`/`LastSeedDuplicates`/`CrossFileDuplicates` are fully removed, no remaining references | Live (review) | `grep -rn "SeedDuplicateRecord\|LastSeedDuplicates\|CrossFileDuplicates" src/ tests/` returns nothing |
| 9 | ❌ | Documentation (README/DOCS.md/CLAUDE.md smoke tests) reflects the new response shapes | Live (review) | Manual read-through of the three docs against the actual endpoint responses |
