# #45 — Import endpoint

**Status:** Waiting for release

**Tiers required:** T1, T2

**GitHub issue:** #45

**Depends on:** #58, #63, #64

---

## Spec requirements (reconciled — see Scope changes)

The original issue text predates #64 (conflict resolution policy) and #63 (import manifest). Before implementation, the spec was reconciled against those shipped features and against the manifest DTOs; the reconciled requirements below are what was actually built. See **Scope changes** for the full list of deviations from the original issue text and why.

1. `POST /api/v1/quotes/import` and `POST /api/v1/quotes/import/preview` — both accept `multipart/form-data` with a required `file` field (one source file) and an optional `settings` JSON text field (`converter`, `duplicateResolution`, `enrich`)
2. `settings.converter` (optional) names a compiled `IQuoteSourceConverter` plugin (e.g. `csv`); omitted means `file` is already Quotinator's canonical JSON schema
3. `settings.duplicateResolution` (optional) is a full policy object (`default` + per-entity-type overrides), overriding `Quotinator:DefaultConflictPolicy` for this run only
4. Duplicate detection and resolution reuse #64's exact 5-policy engine (`FieldMergeResolver`, `QuoteFieldMerge`, `System_ImportConflicts`) against the **live database**, not a second file — same-file repeats are checked first, then the database
5. Quote `id`s are never server-generated — they come from the file itself (deterministic via `QuoteIdentity.StableId` when a source has none of its own, exactly like the bundled converters)
6. A row missing `quote`/`source`, or with an invalid `id`, is skipped and reported in the response's `errors` array — one bad row never aborts the rest of the file
7. One `ImportBatch` row created per non-preview run (`Type = ImportBatchType.Import`), with `ImportBatchId` written on every insert/update
8. `/import/preview` runs the identical pipeline but rolls back every write — no `ImportBatch`, no quote/source/character/person rows, no `System_ImportConflicts` rows
9. Requires `X-Api-Key` matching `Quotinator:AdminApiKey` (not `Authorization: Bearer` — see Scope changes), rate limited under `RateLimitPolicies.Admin`
10. `settings.enrich: true` → `501 Not Implemented`, checked before any file is read (deferred to #19)
11. Malformed `settings` JSON, an unrecognised `converter` name, or file content that converts to zero valid quotes → `422`

**Explicitly out of scope (deferred):** the `/resolve` manual-review endpoint and its audit-log integration — see Scope changes.

---

## Steps

### 1. Add shared settings DTOs; fold per-file `duplicateResolution` into the manifest schema
**Status:** ✅ Done

New `SourceImportSettingsDto` (`Converter` + `DuplicateResolution`) in `Quotinator.Data.Import`, now the base class of `ManifestFileEntryDto` — giving `manifest.json` file entries a new optional per-file `duplicateResolution` override (additive to `schemas/manifest.schema.json`, flat wire shape via inheritance) as a side effect. New `ImportRequestSettingsDto : SourceImportSettingsDto` adds `Enrich`, kept off the shared base so `manifest.json` never gains a nonsensical `enrich` field. Both promoted to `public` (from `internal`) — `Quotinator.Api`'s public `IQuoteImportService` interface needs them in its signature, and C#'s accessibility-consistency check (CS0051) rejects an `internal` type there regardless of `InternalsVisibleTo`.

### 2. Extend `ManifestPolicy.Resolve` to a three-tier cascade
**Status:** ✅ Done

`ManifestPolicy.Resolve(fromHigherTier, fromLowerTier)` is called twice to cascade file → manifest → config, each tier winning wholesale. `SeedFile` gained an optional `Policy` field; `ManifestSeedPlanner.PlanSeed` populates it from each entry's own `duplicateResolution`; `QuotinatorDatabaseInitializer` resolves per-file instead of per-batch in both the real seed and preview paths.

### 3. Grant `Quotinator.Api` visibility into `Quotinator.Data`'s DTOs
**Status:** ✅ Done — superseded by making the DTOs public (step 1); `Quotinator.Data.csproj`'s `InternalsVisibleTo` list was left unchanged from before this issue.

### 4. Add a raw (untranslated) existing-quote lookup query
**Status:** ✅ Done

`Sql.Quotes.SelectRawById()` — no translation joins, no `@lang`. `Sql.Quotes.SelectById()` (used elsewhere for API responses) could not be reused: it COALESCEs in translated content and has no `Genres` column.

### 5. Extract `QuoteSeedWriter` out of `QuotinatorDatabaseInitializer`
**Status:** ✅ Done

`GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/`GetOrCreatePersonAsync`/`InsertTranslationsAsync`/`InsertGenresAsync`/`LogImportConflictAsync` moved to a new internal static `QuoteSeedWriter`, shared by the startup seeder and the new import service. Added `TryGetExistingFieldsAsync` (raw DB lookup, `QuoteFieldMerge`-shaped). Also eliminated a pre-existing duplicate `GenreApiToDb` dictionary in `QuotinatorDatabaseInitializer` in favour of the already-shared `InputValidation.GenreApiToDb`.

**A real bug was found and fixed during this step**, not merely a mechanical move: `GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/`GetOrCreatePersonAsync` only checked their in-memory index, never the database — safe for the seeder (which only ever runs against a guaranteed-empty database) but wrong for a live import against an already-populated one, where it would try to insert a second `Sources` row for a `Title`+`Type` that already exists and hit the unique constraint. Fixed by adding a real `SELECT` (`Sql.Sources.SelectIdByTitleAndType`, `Sql.Characters.SelectIdBySourceAndName`, `Sql.People.SelectIdByName`) on every index-cache miss, in all three methods. Confirmed behavior-preserving for the seeder: `DatabaseInitializerTests`/`ConflictResolutionTests`/`ImportBatchesTests` all pass unmodified after the extraction and the fix.

### 6. Add `Quotinator.Converters.Csv` project
**Status:** ✅ Done

New `IQuoteSourceConverter` plugin (`Name => "csv"`), hand-rolled RFC 4180 parser (`CsvLineParser`, no new NuGet dependency). Columns: `id, quote, originalLanguage, source, date, character, author, type, genres` (`genres` semicolon-delimited); only `quote`/`source` required. Explicit `id` wins when present; otherwise `QuoteIdentity.StableId`. Malformed individual rows are skipped (same silent-skip contract as the two existing converters); `SourceConversionException` only for zero-successful-rows or a missing header/required column. Registered in `Program.cs`'s `quoteSourceConverters` dictionary, `Quotinator.Api.csproj`, `docker/Dockerfile`'s restore-layer `COPY` list, and `Quotinator.slnx`.

### 7. Add response DTOs
**Status:** ✅ Done

`ImportResultResponse`/`ImportSummary`/`ImportConflictEntry`/`ImportRowError` in `Quotinator.Core.Models`.

### 8. Implement `IQuoteImportService` / `SqliteQuoteImportService`
**Status:** ✅ Done

Lives in `Quotinator.Engine.Services`, not `Quotinator.Core.Services` like `IQuoteService` — a deviation from the original design sketch, forced by the fact that its signature needs `ImportRequestSettingsDto` (a `Quotinator.Data` type); Core and Data must never depend on each other, so an interface needing both types must live where both are already legitimately referenced. Uses `SqliteUnitOfWork`/`TransactionScope` (#78) for the shared connection+transaction preview-rollback needs. Two dedicated exception types (`QuoteImportValidationException`, `UnknownConverterException : QuoteImportValidationException`) let the endpoint map failures to `422` without inspecting exception text.

### 9. Register DI
**Status:** ✅ Done — `Program.cs`, alongside the existing converter dictionary and `configPolicy` local.

### 10. Register the two endpoints
**Status:** ✅ Done

`POST /api/v1/quotes/import` and `POST /api/v1/quotes/import/preview` in `QuoteEndpoints.cs`, sharing one private handler. Both require `.DisableAntiforgery()` — ASP.NET Core's antiforgery middleware (`app.UseAntiforgery()`, already active for the Blazor UI) treats any endpoint binding `IFormFile`/`[FromForm]` as requiring a token by default, which would break every non-browser API client using `X-Api-Key` auth instead.

### 11. i18n error message keys
**Status:** ✅ Done — `ApiMessages.cs` + all three `UI.*.json` locales.

### 12. Documentation
**Status:** ✅ Done — `README.md`, `addon/DOCS.md` endpoint tables; `docs/vocabulary.md` gained a `CSV` entry (`ImportBatch`'s existing entry already anticipated "one call to the import endpoint").

### 13. Tests
**Status:** ✅ Done — see Verification below.

---

## Scope changes

Reconciled before implementation — see the existing GitHub comment on #45 (from #64's own reconciliation) for the terminology overlap this builds on. A further comment records the deviations below:

- **Request shape**: the stale issue's `{"format", "conflictStrategy", "data"}` JSON envelope and `?conflictPolicy=`/`?preview=true` query parameters are replaced entirely by `multipart/form-data` (`file` + `settings`), reusing manifest DTOs (`SourceImportSettingsDto`) instead of inventing new fields.
- **Matching is Id-based, not content-based.** The stale spec's "same quote+source text" duplicate matching never existed — #64's already-shipped engine matches by `Id` (deterministic via `QuoteIdentity.StableId`), and #45 reuses that engine unmodified rather than building a second matching algorithm.
- **`conflictStrategy: skip|overwrite|review`** is replaced by #64's five-value `duplicateResolution` policy object, nested in `settings` rather than a top-level field.
- **No `sameId`/`sameText` split in the response.** Since matching is purely Id-based, `sameText` never applies; the response's `conflicts[]` mirrors a `System_ImportConflicts` row directly instead.
- **Two endpoints instead of a `preview=true` flag.** `POST /import` and `POST /import/preview` share one handler; #65's plan doc is updated to match (see that document).
- **Auth is `X-Api-Key`, not `Authorization: Bearer`.** Every other admin-tier endpoint in this codebase (`AdminApiKeyFilter`) already uses `X-Api-Key`; the stale issue text and #15 (not yet implemented) described a mechanism this codebase doesn't actually use anywhere.
- **The `/resolve` endpoint and manual-review workflow are deferred to a new follow-up issue** — it depends on #56 (audit log), which hasn't started. #45 ships detection + automatic policy application only.
- **Per-file `duplicateResolution` override for `manifest.json`** is a bonus, additive side effect of reusing `SourceImportSettingsDto` as `ManifestFileEntryDto`'s base class — not originally requested by #45 or #63, but free once the shared DTO existed.
- **`enrich=true` stays out of scope**, deferred to #19 — already the pre-reconciliation plan's position, restated here.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Both endpoints accept `multipart/form-data` (`file` + optional `settings`) | Unit test | `ImportEndpointTests.Import_CorrectKeyAndValidFile_Returns200WithResultShape`, `ImportPreview_CorrectKeyAndValidFile_Returns200WithPreviewTrue` |
| 2 | ✅ | `settings.converter` selects a registered `IQuoteSourceConverter`; omitted means canonical JSON | Unit test | `QuoteImportServiceTests.ImportAsync_RegisteredConverter_ConvertsBeforeImporting`, `ImportAsync_FreshDatabase_InsertsNewQuote` (no converter) |
| 3 | ✅ | `settings.duplicateResolution` overrides the configured default for this run only | Unit test | `QuoteImportServiceTests.ImportAsync_NewestWins_ReplacesExistingRow` et al. (5 policy tests, each passing an explicit override); `ImportEndpointTests.Import_ValidSettings_PassesConverterAndPolicyThroughToService` |
| 4 | ✅ | Duplicate detection/resolution against the live database, all 5 policies, matches #64's engine | Unit test | `QuoteImportServiceTests.ImportAsync_Skip_KeepsExistingRowUnchanged`, `ImportAsync_NewestWins_ReplacesExistingRow`, `ImportAsync_MergeOurs_TrueConflictKeepsExisting`, `ImportAsync_MergeTheirs_TrueConflictTakesIncoming`, `ImportAsync_Review_BehavesLikeSkip` |
| 5 | ✅ | Ids are never server-generated; deterministic when absent from the source | Unit test | `Quotinator.Converters.Csv.Tests.CsvQuoteConverterTests.ConvertAsync_NoIdColumnValue_DerivesStableId`, `ConvertAsync_ExplicitIdColumn_TakesPrecedenceOverDerivedId` |
| 6 | ✅ | A row missing quote/source or with an invalid Id is skipped and reported in `errors`; rest of file still imports | Unit test | `QuoteImportServiceTests.ImportAsync_OneRowMissingSource_SkipsItButImportsTheRest` |
| 7 | ✅ | One `ImportBatch` row per non-preview run, `Type = Import`, `ImportBatchId` on every write | Unit test | `QuoteImportServiceTests.ImportAsync_FreshDatabase_InsertsNewQuote` (asserts `ImportBatches` count and non-null `BatchId`) |
| 8 | ✅ | Preview rolls back all writes (quotes, `ImportBatches`, `System_ImportConflicts`) | Unit test | `QuoteImportServiceTests.ImportAsync_Preview_LeavesZeroTrace`, `ImportAsync_PreviewWithConflict_NoConflictRowsPersisted` |
| 9 | ✅ | `X-Api-Key` required on both endpoints; `RateLimitPolicies.Admin` applied | Unit test | `ImportEndpointTests.Import_NoKeyConfigured_Returns401`, `Import_MissingAuthHeader_Returns401` (both routes, `DataRow`) |
| 10 | ✅ | `enrich: true` → 501, checked before the file is read | Unit test | `ImportEndpointTests.Import_EnrichTrue_Returns501_BeforeCallingService` (also asserts the service was never called) |
| 11 | ✅ | Malformed `settings` / unrecognised converter / unparseable file content → 422 | Unit test | `ImportEndpointTests.Import_MalformedSettingsJson_Returns422`, `Import_MissingFile_Returns422`, `Import_UnknownConverter_Returns422`, `Import_ServiceThrowsValidationException_Returns422`; `QuoteImportServiceTests.ImportAsync_UnknownConverterName_ThrowsUnknownConverterException`, `ImportAsync_FileWithNoQuotes_ThrowsQuoteImportValidationException`, `ImportAsync_MalformedJson_ThrowsQuoteImportValidationException` |
| 12 | ✅ | Per-file `duplicateResolution` override (bonus) works and cascades file → manifest → config | Unit test | `ManifestSeedPlannerTests.PlanSeed_FileEntryHasDuplicateResolution_SeedFilePolicyIsSet`, `PlanSeed_FileEntryOmitsDuplicateResolution_SeedFilePolicyIsNull`; `ManifestPolicyTests.Resolve_ThreeTierCascade_FileWinsOverManifestWinsOverConfig`, `..._ManifestWinsWhenFileAbsent`, `..._ConfigWinsWhenBothFileAndManifestAbsent` |
| 13 | ✅ | `QuoteSeedWriter` extraction is behavior-preserving; the Sources/Characters/People existence-check bug is fixed | Unit test | `DatabaseInitializerTests`, `ConflictResolutionTests`, `ImportBatchesTests` (all pass unmodified) + `QuoteImportServiceTests.ImportAsync_NewestWins_ReplacesExistingRow` (would fail with `SQLite Error 19` without the fix — reproduced red before fixing) |
| 14 | ✅ | CSV converter: header mapping, id precedence, malformed-row tolerance, missing-column/empty-file failure | Unit test | `Quotinator.Converters.Csv.Tests.CsvQuoteConverterTests` (11 tests) |
| 15 | ✅ | T1 — app starts in VS without error; new endpoints usable | Live | VS run: schema up to date (data v3, app v5), 788 quotes/478 sources/2 characters. `POST /api/v1/admin/database/reset` succeeded (backup, 5 migrations replayed, reseed, 45 duplicates — matches bundled data). `POST /api/v1/quotes/import/preview` (×2, `quotinator-curated.json`) and `POST /api/v1/quotes/import` (same file) all returned `200`. Confirmed 2026-07-05 |
| 16 | ✅ | T2 — `docker build`/`docker run` smoke test; new project included in publish output | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded (`Quotinator.Converters.Csv` present in both restore and publish layers); container started cleanly; `/api/v1/health`, `/api/v1/version` (788 quotes/478 sources, schema v5), `/api/v1/quotes/random` all returned expected results; `POST /api/v1/quotes/import` with a JSON file returned `200` with `conflictPolicy: "newest-wins"`; `POST /api/v1/quotes/import/preview` with a CSV file + `converter: "csv"` + `duplicateResolution.default: "merge-theirs"` returned `200` with `preview: true` and `conflictPolicy: "merge-theirs"`; missing-file and no-key requests correctly returned `422`/`401`. Confirmed 2026-07-05 |

**A real bug was found and fixed during T2 verification**: the response's `conflictPolicy`/`conflicts[].appliedPolicy` fields were serializing as PascalCase (`"NewestWins"`) instead of the kebab-case wire format (`"newest-wins"`) used everywhere else this enum appears in the API (manifest.json, `DuplicateResolutionPolicyJsonConverter`). `Quotinator.Core.Models.ImportResultResponse` can't reference the `DuplicateResolutionPolicy` enum directly (Core/Data isolation), so the fix is a small `ToWireString` helper in `SqliteQuoteImportService` using `JsonNamingPolicy.KebabCaseLower.ConvertName` — the same transform the existing converter applies, just invoked manually since these fields are already plain strings by the time they reach the DTO. Regression-guarded by `QuoteImportServiceTests.ImportAsync_FreshDatabase_InsertsNewQuote` and `ImportAsync_MergeTheirs_TrueConflictTakesIncoming`'s new assertions.

**Full solution `dotnet test --configuration Release`: 814 tests, 0 warnings, 0 errors.** `dotnet build --configuration Release`: 0 warnings, 0 errors.
