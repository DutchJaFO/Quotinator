# #251 — FileResource/FileResourceLine: general import-file content provenance

**Status:** In progress
**GitHub issue:** #251
**Tiers required:** T1, T2
**Depends on:** Nothing

---

## Background

Split out of #227 (2026-08-01) as a genuinely undesigned feature, unrelated to that issue's naming
standardization. This project currently has no record of the actual file(s) an import batch was built
from — `Import_Batch` (post-#253's rename) records `Origin`/`Type` and a few summary fields, but never
the source file's own name, content, or a hash of it. This issue designs and implements that record,
owned by `Quotinator.Data` (generic, domain-agnostic import infrastructure, per ADR 015's `Import_`
domain), matching where `Import_Batch`/`Import_SourceFileOverride` already live.

**Nothing below was settled by the GitHub issue itself** — its own body states the granularity question
"not decided here." The decisions below were worked out with the developer directly (2026-08-02,
after this plan doc's first draft) rather than left as this plan doc's own unilateral recommendation —
see each decision's own note on how it was reached.

---

## Decisions (confirmed with the developer, 2026-08-02)

### 1. Granularity: `Import_FileResourceLine` stores literal text lines, not JSON entries

**Confirmed: line-level granularity is in scope.** The plan doc's first draft recommended rejecting
this, reasoning that "a line" has no stable meaning inside a JSON file. That reasoning was based on a
wrong framing — the developer's actual intent is not "one row per JSON array entry" (which would
indeed duplicate `SourceEntry`/`QuoteIdentity`-derived rows) but "one row per literal newline-delimited
line of the raw file, verbatim" — since every import file this project accepts is plain text (JSON
included), decomposing by line is trivial and format-agnostic: it works identically whether the file is
pretty-printed JSON, a single-line JSON array, or a future CSV/log-style format, with no per-format
special-casing.

**Reconstruction fidelity.** Splitting into lines is lossy on its own — it discards which line-ending
style the file used (`LF` vs `CRLF`) and whether the file ended with a trailing newline. Both are
recorded on the parent `Import_FileResource` row (`LineEnding`, `EndsWithTrailingNewline`) rather than
per line, so a future reconstruct/download endpoint can rebuild either the byte-exact original or a
normalized form, caller's choice. **Confirmed assumption: line endings are uniform within a single
file** — a file mixing `\r\n` and `\n` is out of scope; `LineEnding` records one style per file, not
per line.

### 2. `Origin` needs a third value: files uploaded through the import endpoints

**Confirmed:** this project has three distinct file sources, not two — bundled content (`data/sources/`),
user content (re)seeded from `{dataDir}/imports/`, and files uploaded directly through
`POST /api/v1/import`/`POST /api/v1/import/preview`. The existing `SeedBatchOrigin` enum
(`Bundled`/`UserImports`) only covers the first two, and its own meaning — "where a `SeedBatch`'s files
were discovered from" — doesn't stretch to cover an ad-hoc HTTP upload, which was never part of a
`SeedBatch` at all. A new, dedicated enum is introduced instead of widening `SeedBatchOrigin`:
`Quotinator.Data.Enums.FileResourceOrigin` { `Bundled`, `UserImports`, `Uploaded` }.

### 3. `OriginalFolderPath` is relative to the file's own source root, never an expanded absolute path

**Confirmed:** `OriginalFolderPath` (nullable) records where the file lived *within* its source
category — relative to `data/sources/` for `Bundled`, relative to `{dataDir}/imports/` for
`UserImports`, always `null` for `Uploaded` (confirmed in code: `POST /api/v1/import` binds `IFormFile`,
which carries only a bare filename, never a folder). The path is deliberately **not** the expanded,
`{dataDir}`-inclusive absolute path — storing a root-relative path means a later change to the
deployment's configured `Quotinator:DataDir` (a real possibility — standalone Docker default vs. the HA
supervisor's `/data` mount) never invalidates or reinterprets a historical row, since the stored value
was never tied to the absolute value in the first place.

**Known limitation, accepted as-is:** today's directory scan (`Directory.GetFiles`, confirmed in
`ManifestSeedPlanner.cs`) is non-recursive — neither `data/sources/` nor `{dataDir}/imports/` has real
subfolders today, so `OriginalFolderPath` will be empty for effectively every row until (if ever)
subfolder scanning is added. Included now as a future-proofing field per the developer's explicit ask,
not because it does anything yet.

### 4. Batch-association shape: a lightweight join table, not a column on either side

**Recommendation:** `Import_FileResource` is deduplicated by content hash — re-importing an unchanged
file (the common case: reseeding from the same bundled JSON) does not create a new row, only updates
`LastSeenAtUtc`. This means a single `Import_FileResource` row can legitimately have been the source
for many `Import_Batch` rows over time (e.g. every reseed since the file was last edited). A one-to-one
FK on either table's own columns can't express that, so the design needs a small join table,
`Import_FileResourceBatch (FileResourceId, ImportBatchId, ImportedAt)` — three columns, no content
duplication, growing at the same rate `Import_Batch` itself already grows (proportional to import
events, not to file size or count).

**Alternative considered and rejected:** a `FileResourceId` column directly on `Import_Batch`. Rejected
because a single import call can span multiple source files (e.g. `POST /api/v1/import` with several
files in one request), so the relationship is many-to-many, not many-to-one.

### 5. Pruning policy: keep the N most-recently-seen distinct files, by `FileName`

**Recommendation:** the admin endpoint takes a `keepPerFile` parameter (default from a new
`Quotinator.Constants.Api.QueryParamDefaults` constant, proposed value 5) and, per distinct `FileName`,
deletes every `Import_FileResource` row beyond the N most recent by `LastSeenAtUtc`, cascading the
delete to matching `Import_FileResourceBatch` **and** `Import_FileResourceLine` rows (both FK
`ON DELETE CASCADE`, since a batch's own `Import_Batch` row is the permanent record — only the *file
content copy* is being pruned, not the batch's existence). This is a per-file retention count, not a
global row cap or age-based expiry — simplest to reason about, and directly bounds the thing that
actually grows unboundedly (distinct file *versions* over the file's own edit history), since unchanged
reseeds never add a new row to prune in the first place (per decision 4 above).

No precedent exists elsewhere in this codebase for a pruning/retention mechanism (confirmed via grep —
this is genuinely new ground), so there is no existing pattern to match against; this recommendation is
this plan doc's own proposal, not a "verified against docs" fact the way most of this project's other
design decisions are.

### 6. Converter/ConverterOptions, manifest.json, and rule/alias files (confirmed 2026-08-02, after Steps 1–7 first shipped)

**Found live after Steps 1–7's own T2 pass, in response to the developer asking directly whether an
import file's conversion settings and adjacent files (manifest, rule/alias) were also being captured —
they were not.** Three follow-up decisions, confirmed the same session:

- **Converter + ConverterOptions are new columns on `Import_FileResource` itself** (not `Import_Batch`,
  not the join table) — `Converter` (nullable `TEXT`, the `IQuoteSourceConverter` plugin name) and
  `ConverterOptions` (nullable `TEXT`, raw JSON text — opaque and undeserialized, matching
  `SourceImportSettingsDto.ConverterOptions`'s own treatment). **On a content-hash dedup hit, both
  columns are overwritten with the latest capture's values** (alongside `LastSeenAtUtc`) rather than
  frozen at first capture — confirmed explicitly, since the same raw bytes can legitimately be
  reimported later under different converter settings and the row must reflect the most recent
  interpretation, not go stale.
- **`manifest.json`'s own content is captured too** — as its own `Import_FileResource` row (Origin
  matches the batch's own), linked to every `Import_Batch` it drove in that seed pass (one
  `Import_FileResourceBatch` row per batch, via the existing many-to-many join table — no schema
  change needed for this part). Requires `SeedBatch.SourceDirectory` (new, optional field — see below)
  rather than the individual `SeedFile.FilePath`'s own directory, since `ISourceCacheUpdater` rewrites a
  downloaded file's `FilePath` to a separate cache directory that never contains `manifest.json` —
  found live via a T2 pass showing the manifest linked to only 2 of 4 bundled batches before this fix.
- **`RuleFilePath`/`SourceAliasFilePath` capture is deferred to #252** — #252 is specifically about
  whether #153's `Import_SourceFileOverride` (the existing mechanism tracking these exact files) should
  be superseded by `FileResource`, making it the natural home for this decision rather than folding it
  into #251.

**Schema/code changes made directly to Step 3's already-implemented (but still unshipped) migration —
not a new migration, matching the same "correction before anything shipped" precedent as decision 1's
`Import_FileResourceLine`/`Import_FileResourceBatch` composite-PK fix:**
- `Import_FileResource` gains `Converter TEXT`/`ConverterOptions TEXT` (both nullable, no CHECK — free
  text, not a closed enum set).
- `SeedBatch` (`Quotinator.Data.Import`) gains an optional 5th field, `SourceDirectory` (`string?`,
  default `null`) — set to `bundledDir`/`importsDir` in `SeedBatchesBuilder.Build`, `null` for any
  `SeedBatch` built directly (e.g. by a test) rather than via that builder. Purely additive — no
  existing `SeedBatch` construction call site needed to change.
- `QuotinatorDatabaseInitializer.CreateImportBatchAsync` derives `manifest.json`'s path from
  `seedBatch.SourceDirectory ?? Path.GetDirectoryName(seedFile.FilePath)` — the fallback keeps existing
  tests that construct a bare `SeedBatch` working unchanged.
- `IFileResourceRepository.WriteAsync` gains two new optional parameters, `converter`/`converterOptions`
  (both `string?`, default `null`), inserted before the existing `cancellationToken` — every call site
  updated to use named `cancellationToken:` since C# requires optional parameters to trail.
- `SqliteQuoteImportService.ImportAsync` passes `settings?.Converter`/`settings?.ConverterOptions?.GetRawText()`
  (the upload path's own settings — genuinely needed to interpret the captured raw bytes).
  `QuotinatorDatabaseInitializer.CreateImportBatchAsync` passes `seedFile.Converter`/
  `seedFile.ConverterOptions?.GetRawText()` (the seed path's own manifest-declared settings — provenance
  of how the captured, already-canonical on-disk file was produced by the download/cache step, not
  something still needed to interpret the captured bytes themselves, since seeding never re-applies a
  converter at read time).

**T2-verified against a real Docker container** with the two bundled sources that actually declare a
converter (`NikhilNamal17_popular-movie-quotes.json` → `basic-json-array`,
`vilaboim_movie-quotes.json` → `regex-array`): both `Converter` and the full `ConverterOptions` JSON
(including nested objects) round-tripped correctly, `manifest.json` was captured as its own row, and —
after the `SeedBatch.SourceDirectory` fix — linked to all 4 bundled batches, not just the 2 whose files
were never cache-redirected.

---

## Proposed schema

```sql
CREATE TABLE IF NOT EXISTS Import_FileResource (
    Id                     TEXT    NOT NULL PRIMARY KEY,
    FileName               TEXT    NOT NULL,
    OriginalFolderPath     TEXT,
    Origin                 TEXT    NOT NULL CHECK (Origin IN ('Bundled', 'UserImports', 'Uploaded')),
    ContentHash            TEXT    NOT NULL,
    LineEnding             TEXT    NOT NULL CHECK (LineEnding IN ('LF', 'CRLF', 'CR')),
    EndsWithTrailingNewline INTEGER NOT NULL,
    Converter              TEXT,
    ConverterOptions       TEXT,
    FirstSeenAtUtc         TEXT    NOT NULL,
    LastSeenAtUtc          TEXT    NOT NULL,
    DateCreated            TEXT    NOT NULL,
    DateModified           TEXT,
    DateDeleted            TEXT,
    IsDeleted              INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_FileResource_ContentHash ON Import_FileResource (ContentHash);
CREATE INDEX IF NOT EXISTS IX_Import_FileResource_FileName ON Import_FileResource (FileName);

CREATE TABLE IF NOT EXISTS Import_FileResourceLine (
    Id             TEXT    NOT NULL PRIMARY KEY,
    FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
    LineNumber     INTEGER NOT NULL,
    Text           TEXT    NOT NULL,
    DateCreated    TEXT    NOT NULL,
    DateModified   TEXT,
    DateDeleted    TEXT,
    IsDeleted      INTEGER NOT NULL DEFAULT 0,
    UNIQUE (FileResourceId, LineNumber)
);

CREATE TABLE IF NOT EXISTS Import_FileResourceBatch (
    Id             TEXT    NOT NULL PRIMARY KEY,
    FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
    ImportBatchId  TEXT    NOT NULL REFERENCES Import_Batch(Id),
    ImportedAt     TEXT    NOT NULL,
    DateCreated    TEXT    NOT NULL,
    DateModified   TEXT,
    DateDeleted    TEXT,
    IsDeleted      INTEGER NOT NULL DEFAULT 0,
    UNIQUE (FileResourceId, ImportBatchId)
);
```

- `Origin` is the new `Quotinator.Data.Enums.FileResourceOrigin` enum (decision 2 above) — `Bundled`/
  `UserImports`/`Uploaded` — via the project's standard `SafeValue<TEnum?>` + `RegisterEnumHandler<TEnum>`
  pattern. CHECK constraint per ADR 008.
- `OriginalFolderPath` is nullable and root-relative, never the expanded absolute path (decision 3
  above) — `null` for every `Uploaded` row.
- `ContentHash` is SHA-256 of the raw file bytes (hex-encoded), matching the "content hash" language in
  the issue body, computed the same way regardless of the line/granularity decision (decision 1 above).
  Unique index enforces the dedup-by-content invariant from decision 4.
- `LineEnding`/`EndsWithTrailingNewline` record reconstruction fidelity (decision 1 above) — one value
  per file, not per line (the confirmed uniform-line-endings assumption). `LineEnding` is a new
  `Quotinator.Data.Enums.LineEndingStyle` enum (`LF`/`CRLF`/`CR`), same `SafeValue<TEnum?>` +
  `RegisterEnumHandler<TEnum>` pattern as `Origin`.
- **No `Content` column** — the previous draft's single-blob column is replaced entirely by
  `Import_FileResourceLine`; nothing duplicates the raw text in two places.
- `Import_FileResourceLine` stores one row per literal line of the original file, `Text` being that
  line's content with the line terminator itself stripped (the terminator is reconstructed from the
  parent row's `LineEnding` at read time, not stored per line).
- **Correction (2026-08-02, found before any code shipped):** an earlier draft of this schema gave
  `Import_FileResourceLine`/`Import_FileResourceBatch` a bare composite primary key and no `RecordBase`
  columns, reasoning from an invented "`CharacterSources`-style pure-link-row precedent" that turned out
  not to exist — `Quotinator_CharacterSource`/`Quotinator_QuoteGenre` (checked directly in
  `QuotinatorMigrations.cs`) both carry a full `RecordBase` shape with the natural key enforced as a
  separate `UNIQUE` constraint, exactly per **ADR 002** ("RecordBase applies to all tables without
  exception" — junction/child-row tables are explicitly the case that ADR calls out as not exempt, per
  its own worked `QuoteTag` example). Both child tables now carry a surrogate `Id` plus full
  `RecordBase` columns, matching that precedent for real.
- References `Import_Batch(Id)` (#253's renamed table) — this migration must land after #253's, or in
  the same migration if implemented on the same branch after #253 merges. Table names in this schema
  already assume #253 has shipped.

---

## Steps

### 1. Confirm the design decisions above with the developer

**Status:** ✅ Done

All five decisions above confirmed with the developer 2026-08-02 (including a reversal of this plan
doc's own first-draft recommendation on granularity — see decision 1's note). No longer a blocking gate.

### 2. Add the new `FileResourceOrigin`/`LineEndingStyle` enums

**Status:** ✅ Done

`Quotinator.Data.Enums.FileResourceOrigin` { `Bundled`, `UserImports`, `Uploaded` } and
`Quotinator.Data.Enums.LineEndingStyle` { `LF`, `CRLF`, `CR` } — two new files, following #255's
Enums-folder convention directly (no `Models`/`Entities`/`Import` placement mistake to correct later).
Both registered via `RegisterEnumHandler<TEnum>()` in `DatabaseConfiguration.Configure()`.

### 3. Write the migration and baseline

**Status:** ✅ Done

New `Quotinator.Data` migration (next version after #253's rename) creating all three tables per the
schema above. Update `DataBaselineSql` to match, per CLAUDE.md's baseline-drift rule.

### 4. Implement the write path

**Status:** ✅ Done

`IFileResourceRepository`/`SqliteFileResourceRepository` implemented in `Quotinator.Data`, wired into
DI in `Program.cs`. `WriteAsync` computes the content hash, dedups against an existing row (updates
`LastSeenAtUtc` only) or splits the content via a new `FileContentSplitter` helper (detects
`LineEnding`/`EndsWithTrailingNewline`, matching decision 1) and inserts the parent row plus its
`Import_FileResourceLine` rows, then always inserts an `Import_FileResourceBatch` link row. Hooked into
both real pipelines: `QuotinatorDatabaseInitializer.CreateImportBatchAsync` (Bundled/UserImports, per
`SeedBatchOrigin`) and `SqliteQuoteImportService.LoadSourceFileAsync`/`ImportAsync` (Uploaded, capturing
the raw pre-conversion multipart content). Twelve existing test files plus `Program.cs` updated to pass
a new `NoOpFileResourceRepository` test double at the two constructors' new parameter.

Found and fixed during verification (not part of the original design, but required for the full suite
to go green):
- The `Sql.FileResources.*` query set initially had four queries with bare, unwrapped `Id`
  columns/comparisons — fixed to use `IdClauses.Equals`/`IdClauses.SelectColumn` throughout, per this
  project's case-insensitive-id convention (`SqlIdCaseGuard`/`SqlSelectPresentationGuard`).
- `Sql_ContainsOnlyGenericInfrastructureQueries` needed `FileResources` added to its allowlist — it
  never touches a consumer-defined entity (only `Import_FileResource`/`Import_FileResourceLine`/
  `Import_FileResourceBatch`/`Import_Batch`), so it correctly stays in `Quotinator.Data.Queries.Sql`
  alongside `ImportBatches`/`SystemSourceFileOverrides`.
- `SqliteFileResourceRepository.PruneAsync` originally had an inline `DELETE FROM Import_FileResource
  WHERE Id IN @ids;` string literal — centralised into `Sql.FileResources.DeleteByIds` (via
  `IdClauses.In`) per the string-centralisation policy; `AllSqlStringLiterals_AreInCentralisedFiles`
  caught this.
- `Microsoft.Data.Sqlite` defaults `foreign_keys = ON` per connection (unlike raw SQLite's own
  off-by-default) — discovered via failing repository tests; fixed by inserting a real `Import_Batch`
  row in the test fixture before every `WriteAsync` call instead of a bare `Guid.NewGuid()`.
- `LastSeenAtUtc` has only second-level precision, so two writes within the same test method produced
  an unspecified tie-break order in the prune query's `ROW_NUMBER() OVER (... ORDER BY LastSeenAtUtc
  DESC)` — fixed by adding `, rowid DESC` as a secondary sort key.
- Three test assertions elsewhere in the suite hardcoded the Data-owned migration count as `5`; updated
  to `6` to reflect the new FileResource migration
  (`ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental`,
  `InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations`,
  `InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental`).

Full solution (before decision 6's follow-up work): `dotnet build --configuration Release -nodeReuse:false`
— 0 warnings/0 errors; `dotnet test --configuration Release -nodeReuse:false` — 2905/2905 passed.
After decision 6 (Converter/ConverterOptions + manifest.json capture + `SeedBatch.SourceDirectory`):
2917/2917 passed — see Step 8 for the final numbers.

### 5. Implement the reconstruct/download endpoint

**Status:** ✅ Done

`GET /api/v1/import/file-resources/{id}/download` implemented in `publicGroup` — read-only, no
`X-Api-Key` required, matching `GET /admin/audit`'s own precedent (the plan's earlier "in the
`adminGroup`" note was a draft-stage inconsistency: nothing about this endpoint mutates data, and unlike
Step 6 it was never described as destructive). Reassembles `Import_FileResourceLine` rows (ordered by
`LineNumber`) into the original text via `FileContentSplitter.Join`, using the stored `LineEnding`/
`EndsWithTrailingNewline` by default. Accepts an optional `lineEnding` query parameter — generalised to
accept all three `LineEndingStyle` values (`lf`/`crlf`/`cr`, case-insensitive) rather than only
`lf`/`crlf` as originally drafted, since restricting out a legitimately storable style had no real
justification. `EndsWithTrailingNewline` is never overridden by the query parameter. Returns `404` for
a malformed id or one with no matching row. Two new `ApiMessages` keys (`FileResourceNotFound`,
`LineEndingInvalid`) added with `en-GB`/`nl`/`de` translations.

**Moved out of `AdminEndpoints.cs` entirely on 2026-08-03**, per the developer's T1 review: a captured
file's own content is import infrastructure, not database administration, so the route/tag/file should
match `/api/v1/import/rules/*`'s own precedent rather than living under `/api/v1/admin/*` just because
the handler happened to be added to `AdminEndpoints.cs` first. Now `GET /api/v1/import/file-resources/{id}/download`
in a new `ImportFileResourceEndpoints.cs`, tagged `ApiTags.Import`. See the "Route reorganization" note
after Step 8 for the full change set.

### 6. Implement the pruning mechanism and admin endpoint

**Status:** ✅ Done

`POST /api/v1/import/file-resources/prune` implemented in `adminGroup` (destructive — requires
`X-Api-Key`, `RateLimitPolicies.Admin`), implementing decision 5's per-`FileName` retention sweep via
the existing `IFileResourceRepository.PruneAsync`. Returns `FileResourcePruneResponse { PrunedCount }`.
`keepPerFile` follows the numeric query parameter binding pattern (`string?`-bound, `int.TryParse`, 422
via the new `KeepPerFileInvalid` message on malformed/negative input), with its default
(`QueryParamDefaults.KeepPerFile = 5`) registered in `NumericParameterSchemaTransformer.NumericParamsByPath`
for `api/v1/import/file-resources/prune`. Moved alongside the download endpoint into
`ImportFileResourceEndpoints.cs` on 2026-08-03 — see the "Route reorganization" note after Step 8.

Found and fixed during verification: the `keepPerFile` parameter initially had no C# `= null` default
(only the `[DefaultValue]` attribute) — every other numeric-string query parameter in this codebase
carries an explicit `= null`, and omitting it caused `Microsoft.AspNetCore.OpenApi`'s schema generator to
try to write the attribute's boxed `int` default into a `string`-typed schema, throwing
`InvalidCastException` and taking down `/openapi/v1.json` for every path, not just this one (a single
broken parameter's schema generation failure fails the whole shared document). Fixed by reordering the
parameter list so DI-injected services come first and `keepPerFile = null` trails them, matching the
`/audit` endpoint's own precedent for the same reason (C# requires optional parameters to trail).

### 7. Tests

**Status:** ✅ Done

Superseded the GitHub issue's own Expected tests table (written before the granularity/origin/folder
decisions above existed) — the issue itself was updated 2026-08-02 to match. All 15 planned tests
implemented and passing, plus a new `FakeFileResourceRepository` test double
(`tests/Quotinator.Api.Tests/Fakes/`) for the endpoint tests, matching the existing `Fake*Registry`
pattern:

- `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalFileResourceSchema` ✅
- `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameFileResourceCheckConstraintValues` ✅ (not in the original list — added alongside the schema test, matching every other Data-owned table's existing pair)
- `SqliteFileResourceRepositoryTests.WriteAsync_UnchangedFileContent_DoesNotDuplicateRow` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_ChangedFileContent_CreatesNewRow` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_LinksFileResourceToImportBatch` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_SplitsContentIntoOrderedFileResourceLineRows` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_DetectsCrlfLineEndingAndTrailingNewline` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_DetectsLfLineEndingNoTrailingNewline` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_UploadedOrigin_StoresNullOriginalFolderPath` ✅
- `SqliteFileResourceRepositoryTests.PruneAsync_KeepsOnlyKeepPerFileMostRecentRowsPerFileName` ✅
- `SqliteFileResourceRepositoryTests.PruneAsync_CascadesDeleteToFileResourceLineAndBatchLinks` ✅
- `AdminEndpointsTests.DownloadFileResource_ReconstructsOriginalLineEndingByDefault` ✅
- `AdminEndpointsTests.DownloadFileResource_LineEndingOverride_NormalizesOutput` ✅
- `AdminEndpointsTests.DownloadFileResource_UnknownId_Returns404` ✅
- `AdminEndpointsTests.PruneFileResources_NoApiKey_Returns401` ✅
- `AdminEndpointsTests.PruneFileResources_MalformedKeepPerFile_Returns422` ✅

**Added for decision 6's follow-up scope (Converter/ConverterOptions + manifest.json capture), not part
of the original 15:**

- `SqliteFileResourceRepositoryTests.WriteAsync_ConverterAndConverterOptionsSupplied_AreStoredOnTheRow` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_NoConverterSupplied_LeavesConverterColumnsNull` ✅
- `SqliteFileResourceRepositoryTests.WriteAsync_DedupHitWithDifferentConverter_OverwritesWithTheLatestValues` ✅
- `FileResourceCaptureTests.InitialiseAsync_ManifestJsonPresentInSourceDir_CapturesItsOwnContentLinkedToTheBatch` ✅
- `FileResourceCaptureTests.InitialiseAsync_SeedFilePathRedirectedToCacheDir_StillFindsManifestViaSourceDirectory` ✅
- `FileResourceCaptureTests.InitialiseAsync_NoManifestJsonInSourceDir_DoesNotCaptureAManifestRow` ✅
- `FileResourceCaptureTests.InitialiseAsync_SeedFileWithConverterAndOptions_CapturesThemOnTheFileResourceRow` ✅

### 8. Full solution build, test, and Docker verification

**Status:** ✅ Done

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. ✅
`dotnet test --configuration Release -nodeReuse:false` — **2917/2917 passed**, 0 warnings, 0 errors
(final count, including decision 6's 7 additional tests). ✅
`docker build -f docker/Dockerfile -t quotinator:local .` — succeeded. ✅ T2 smoke-tested against a
live container across two separate passes (see `docs/smoke-tests.md` §30, added per the living-checklist
rule):
- **First pass (Steps 1–7 scope):** all four bundled source files captured with correct `Bundled` origin
  on startup; the download endpoint reconstructs `quotinator-curated.json` **byte-for-byte identical** to
  the original on disk (confirmed via `diff`); the `lineEnding=crlf` override verified via hex dump to
  actually emit `\r\n` where the file was captured as bare `LF`; unknown id → `404`; invalid
  `lineEnding` → `422`; prune with no/wrong key → `401`; prune with malformed `keepPerFile` → `422`;
  prune with a valid key → `200 {"prunedCount":0}`.
- **Second pass (decision 6 scope, after the developer asked whether converter settings/manifest were
  also captured):** `basic-json-array`/`regex-array` converter names and their full `ConverterOptions`
  JSON (nested objects included) round-tripped correctly for the two bundled sources that declare a
  converter; `manifest.json` captured as its own row; confirmed linked to only 2 of 4 batches before the
  `SeedBatch.SourceDirectory` fix, then re-verified linked to all 4 after it.

**T1 confirmed 2026-08-03** — developer ran the app in Visual Studio: schema migrated v5→v6 cleanly,
seeding completed, app reached ready state and served requests correctly (`GET
/admin/database/seed/preview` returned the expected per-file report shape). One non-issue surfaced
during the run: `UnresolvedFieldConflictException` (thrown by `FieldMergeResolver.cs:144`, caught in
four places in `ImportActionPlanner.cs` as normal "fall through to Pending staging" control flow —
unrelated to #251, code untouched by this issue) popped a Visual Studio "break on this exception type"
dialog repeatedly; not a bug, just a noisy debugger setting.

## Route reorganization (2026-08-03, after T1 review)

**Both endpoints moved out of `AdminEndpoints.cs`/`/api/v1/admin/*` into a new
`ImportFileResourceEndpoints.cs`/`/api/v1/import/file-resources/*`**, per the developer's direct
feedback during T1 review: a captured file's own content is import infrastructure, not database
administration, so it belongs alongside `/api/v1/import/rules/*` (`ImportRuleEndpoints.cs`), not mixed
into the admin surface just because the handler was originally added there. Final routes:
- `GET /api/v1/import/file-resources/{id}/download` (`publicGroup`, no `X-Api-Key`, unchanged behaviour)
- `POST /api/v1/import/file-resources/prune` (`adminGroup`, requires `X-Api-Key`, unchanged behaviour)

Tagged `ApiTags.Import` instead of `ApiTags.Admin`. `NumericParameterSchemaTransformer.NumericParamsByPath`'s
key updated to `api/v1/import/file-resources/prune`. Tests moved from `AdminEndpointsTests.cs` into a
new `ImportFileResourceEndpointsTests.cs` (matching `ImportRuleEndpointsTests.cs`'s precedent), routes
updated, otherwise unchanged — same 5 tests, same assertions. README.md/`addon/DOCS.md`/`addon-beta/DOCS.md`
endpoint tables updated to move both rows into the Import section. `docs/smoke-tests.md` §30's curl
commands updated to the new routes.

---

## GitHub issue update needed

**Status:** ✅ Done — #251's body was updated 2026-08-02 (before implementation started) to match the
decisions above, replacing the original "decide whether line-level granularity is needed" open question
and the pre-decision Expected tests table.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Design decisions (granularity, origin, folder path, reconstruction fidelity, batch association) made and recorded | Live | This plan doc's "Decisions (confirmed with the developer, 2026-08-02)" section |
| 2 | ✅ | Schema implemented (migration + baseline updated together) | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalFileResourceSchema` |
| 3 | ✅ | Re-importing unchanged file content does not duplicate the `Import_FileResource` row | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_UnchangedFileContent_DoesNotDuplicateRow`, `SqliteFileResourceRepositoryTests.WriteAsync_ChangedFileContent_CreatesNewRow` |
| 4 | ✅ | A batch's originating file(s) are queryable after import | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_LinksFileResourceToImportBatch` |
| 5 | ✅ | Content is decomposed into ordered `Import_FileResourceLine` rows, with line-ending style and trailing-newline presence recorded on the parent row | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_SplitsContentIntoOrderedFileResourceLineRows`, `...DetectsCrlfLineEndingAndTrailingNewline`, `...DetectsLfLineEndingNoTrailingNewline` |
| 6 | ✅ | All three file origins (Bundled/UserImports/Uploaded) are recorded correctly, including `OriginalFolderPath` being root-relative and null for uploads | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_UploadedOrigin_StoresNullOriginalFolderPath` |
| 7 | ✅ | A file resource's original content can be reconstructed, honouring or overriding its recorded line-ending style | Endpoint test | `ImportFileResourceEndpointsTests.DownloadFileResource_ReconstructsOriginalLineEndingByDefault`, `...LineEndingOverride_NormalizesOutput`, `...UnknownId_Returns404` |
| 8 | ✅ | Pruning mechanism implemented and exposed via an admin endpoint, cascading to both child tables | Unit test | `SqliteFileResourceRepositoryTests.PruneAsync_KeepsOnlyKeepPerFileMostRecentRowsPerFileName`, `SqliteFileResourceRepositoryTests.PruneAsync_CascadesDeleteToFileResourceLineAndBatchLinks` |
| 9 | ✅ | Prune endpoint follows this project's admin conventions | Endpoint test | `ImportFileResourceEndpointsTests.PruneFileResources_NoApiKey_Returns401`, `ImportFileResourceEndpointsTests.PruneFileResources_MalformedKeepPerFile_Returns422` |
| 10 | ✅ | Full solution builds and tests pass | Live | `dotnet build --configuration Release -nodeReuse:false` and `dotnet test --configuration Release -nodeReuse:false` both 0 warnings, 0 errors, all green |
| 11 | ✅ | T1 verified | Live | Developer started app in Visual Studio 2026-08-03 — clean v5→v6 migration, seeding completed, app served requests correctly |
| 12 | ✅ | T2 verified | Live | `docs/smoke-tests.md` §30 — byte-exact download reconstruction, `lineEnding` override, error cases, and prune auth/validation, all confirmed against a live Docker container |

---

## Scope changes

**Design revised 2026-08-02, before any implementation started.** The plan doc's first draft
recommended rejecting line-level granularity and used only two `Origin` values with no folder-path
field; the developer's actual intent (three real file sources including ad-hoc uploads, plus wanting
byte-faithful reconstruction) needed all of: `Import_FileResourceLine` accepted after all (with
corrected reasoning — literal text lines, not JSON-array-entry decomposition), a new `FileResourceOrigin`
enum with a third `Uploaded` value, a root-relative `OriginalFolderPath` column, and
`LineEnding`/`EndsWithTrailingNewline` reconstruction-fidelity columns. See "Decisions (confirmed with
the developer, 2026-08-02)" above for the full reasoning behind each change. Nothing here was
implemented before the revision — no rework, this is the design settling before Step 2 begins.

**Second revision, 2026-08-03, after Steps 1–7 were already implemented and T2-verified.** The
developer asked directly whether an import's conversion settings and adjacent files (manifest, rule/
alias) were also being captured — they were not; only the primary source file's raw content was. This
led to decision 6 above: `Converter`/`ConverterOptions` columns on `Import_FileResource` (overwritten on
a dedup hit with the latest values), `manifest.json`'s own content captured and linked to every batch it
drove (which itself needed a new `SeedBatch.SourceDirectory` field once a second T2 pass showed the
initial implementation only linking 2 of 4 batches), and `RuleFilePath`/`SourceAliasFilePath` capture
explicitly deferred to #252. Applied directly to Step 3's still-unshipped migration (a correction, not a
new migration) — see decision 6's own "Schema/code changes" list for the full change set. Steps 4–8's
own sections above were updated in place to reflect the final, post-revision implementation rather than
narrating the two passes separately.

## Relationship to #252

#252 (confirm whether #153's `Import_SourceFileOverride` should be superseded by this mechanism) is
blocked on this issue reaching an implemented, confirmed schema — see #252's own plan doc.
