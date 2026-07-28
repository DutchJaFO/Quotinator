# #64 — Conflict resolution policy

**Status:** Waiting for release

**Tiers required:** T1, T2

**GitHub issue:** #64

**Depends on:** #63, #45, #58

---

## Spec requirements

1. Five policies: `skip`, `newest-wins`, `merge-ours`, `merge-theirs`, `review`. `review` behaves identically to `skip` today — reserved for #45's future human-review workflow, added now so no further schema/enum change is needed when #45 lands.
2. Default policy: `newest-wins`.
3. Global config key: `Quotinator__DefaultConflictPolicy` (flat); per-entity-type overrides (`Quotes`/`Sources`/`Characters`/`People`/`Translations`) stay nested under `Quotinator:DuplicateResolution:*`.
4. Manifest-level `duplicateResolution` override (per-source, per-type) — accepted scope expansion beyond #64's original text, built for #57/#61, documented via GitHub comment (see Scope changes below).
5. `merge-ours`/`merge-theirs` behaviour: per field, auto-fill from whichever side is non-empty when the other is empty/null. When both sides have differing non-empty values (a true conflict), `merge-ours` keeps the existing value and `merge-theirs` takes the incoming value. Applies identically to scalar and array/list fields — no union/combine behavior.
6. `ImportBatches.ConflictPolicy` — new column, modeled as `SafeValue<DuplicateResolutionPolicy?>` (enum, via the existing `SafeEnumHandler<TEnum>` infrastructure), recording the batch's representative applied policy (quotes' effective policy, since a batch may span multiple entity types but the issue asks for one column).
7. New `System_ImportConflicts` table, owned by `Quotinator.Data` (own migration list, `System_SchemaVersion`, excluded from Reset via the existing `System_%` pattern match) — logs **every** detected conflict, not only pending ones, with a `Status` (`resolved`/`pending`). `ExistingValue`/`IncomingValue` are opaque JSON blobs (Data never parses them). `AppliedPolicy` is the same enum/`SafeValue` pattern as #6. `MergedFields` is an optional opaque JSON blob populated only for `merge-ours`/`merge-theirs` resolutions, documenting per-field which side won.
8. Per-import-run override via query parameter — blocked on #45 (not started); left out of scope, no speculative parameter added.
9. Known limitation, accepted for now: the single generic conflict table assumes one import "domain" (quotes and related entities). Revisit if Quotinator ever adds a second, structurally distinct import domain.

---

## Steps

### 1. Rename `Overwrite` → `NewestWins`; complete the five-value vocabulary
**Status:** ✅ Done — enum now has all five members; `ManifestSeedPlannerTests` updated. Wire-string kebab-case assertion happens in step 5, where the JSON converter is fixed.

`DuplicateResolutionPolicy` enum gains all five members: `Skip`, `NewestWins`, `MergeOurs`, `MergeTheirs`, `Review`. Wire strings (JSON and config): `"skip"`, `"newest-wins"`, `"merge-ours"`, `"merge-theirs"`, `"review"` — via `JsonNamingPolicy.KebabCaseLower` (must be asserted for the multi-word values, not assumed, e.g. `MergeOurs` → `"merge-ours"`). `ManifestSeedPlannerTests`' `Overwrite` reference updates to `NewestWins`.

### 2. Flip defaults to `NewestWins`
**Status:** ✅ Done — `ManifestPolicy.HardcodedDefault` and `ManifestPolicyDto.Default`'s field default both changed. Confirmed `data/sources/manifest.json`'s explicit `"skip"` override is untouched.

`ManifestPolicy.HardcodedDefault` and `ManifestPolicyDto.Default`'s omitted-key default both become `NewestWins` (currently `Skip`). `data/sources/manifest.json`'s explicit `"skip"` stays untouched — it's a deliberate per-source override, unaffected by the global default changing.

### 3. Update `schemas/manifest.schema.json`'s enum values
**Status:** ✅ Done

`duplicateResolution` enum values become `["skip", "newest-wins", "merge-ours", "merge-theirs", "review"]`.

### 4. Restructure config keys
**Status:** ✅ Done — `Program.cs` reads flat `Quotinator:DefaultConflictPolicy`; nested per-type keys unchanged; `schemas/manifest.schema.json`'s config-key reference updated to match. `appsettings.local.json` (gitignored, user-owned) still has the old nested `DuplicateResolution:Default` key — flagged to the user, not edited here.

`Program.cs`'s `ParseResolutionPolicy`/`ParseNullableResolutionPolicy` read a new flat `Quotinator:DefaultConflictPolicy` key (env `Quotinator__DefaultConflictPolicy`, default `newest-wins` when absent), replacing `Quotinator:DuplicateResolution:Default`. The 5 nested per-type keys (`Quotinator:DuplicateResolution:{Quotes,Sources,Characters,People,Translations}`) keep their paths, minus the now-redundant `Default` sibling. String matching covers all five wire values.

### 5. Fix JSON kebab-case enum serialization
**Status:** ✅ Done — `DuplicateResolutionPolicyJsonConverter` added; wired onto all `ManifestPolicyDto` properties; round-trip test added in `DuplicateResolutionPolicyJsonConverterTests`.

A small `DuplicateResolutionPolicyJsonConverter : JsonStringEnumConverter<DuplicateResolutionPolicy>(JsonNamingPolicy.KebabCaseLower)` (parameterless-constructor subclass, since the attribute form can't pass constructor arguments directly), referenced on all `ManifestPolicyDto` properties. Verified via an actual round-trip unit test for all five values — a plain `JsonStringEnumConverter` would only case-insensitively match member names, not hyphenate `"newest-wins"`, so this must not be assumed correct.

### 6. Rename `Sql.Quotes.UpdateOnOverwrite` → `UpdateOnNewestWins`
**Status:** ✅ Done — renamed in `Sql.cs` and its call site. No `SqlQueryGuardTests` reference existed for this const (it's a fixed query, not a dynamic factory method — guard tests only cover factory methods per CLAUDE.md's SQL centralisation policy), so there was nothing to update there.

### 7. Implement `merge-ours`/`merge-theirs` field-level resolution
**Status:** ✅ Done

`FieldMergeResolver` (`Quotinator.Data/Import/FieldMergeResolver.cs`) implements the generic per-field comparison described in the spec, with 13 passing unit tests covering auto-fill, true-conflict tie-breaks, and array-field no-union behaviour. `QuoteFieldMerge` (`Quotinator.Engine/Database/QuoteFieldMerge.cs`) converts a `SourceQuote` to/from the field-name→value map for `QuoteText`, `OriginalLanguage`, `Source`, `Date`, `Character`, `Author`, `Type`, `Genres`. **Scope note:** `Id` and `Translations` are excluded from the merge set — `Id` is the join key (always equal on both sides), and per-language translation merging is a distinct, unspecced feature; the merged quote always carries the incoming side's `Translations` unconditionally, same as the pre-existing newest-wins path already did.

`QuotinatorDatabaseInitializer.SeedIfEmptyInternalAsync`'s duplicate-handling branch is now a full 5-way switch: `Skip`/`Review` skip (unchanged), `NewestWins` runs the pre-existing overwrite path unchanged, `MergeOurs`/`MergeTheirs` build a merged `SourceQuote` via the above and run the same overwrite pipeline against it. `seenIds` now tracks `(FilePath, SourceQuote)` instead of just the file path, so a merge has the first-seen record's actual field values to merge against (previously only the file path was retained, since `NewestWins`/`Skip` never needed the content).

**Fallout discovered while wiring this in:** flipping `HardcodedDefault` to `NewestWins` (step 2) changes real seeding behaviour for `DatabaseInitializerTests.InitialiseAsync_AllSourceFiles_SeedsExpectedCounts` — `SourceCount` shifts from 478 to 479, because at least one of the 45 cross-file duplicates has a later-file source title that didn't match the first-seen source index entry. This is a genuine, correct behaviour change from flipping the default (not a bug in the switch rewrite) — fixed together with the other known `DatabaseInitializerTests` fallout in step 11.

### 8. Add `ImportBatches.ConflictPolicy` column
**Status:** ✅ Done — `Migration005_ImportBatchConflictPolicy` added; `ImportBatch.ConflictPolicy` (`SafeValue<DuplicateResolutionPolicy?>`) added; `RegisterEnumHandler<DuplicateResolutionPolicy>()` wired into `QuotinatorDapperConfiguration`; populated in `CreateImportBatchAsync` from `seedBatch.Policy.ForQuotes`. Insert/read go through Dapper.Contrib's reflection-based `InsertAsync<T>` and `SELECT *`, so no manual SQL column list needed. `QuotinatorMigrations.Baseline` is not yet updated to match — the schema-drift test is expected red until step 10.

New `Migration005_ImportBatchConflictPolicy` in `QuotinatorMigrations.cs`: `ALTER TABLE ImportBatches ADD COLUMN ConflictPolicy TEXT NOT NULL DEFAULT 'skip'` (backfill value for pre-existing rows only — new rows get their real applied policy at insert time), following the established plain-`ALTER` pattern from Migration003 (no idempotency guard needed, per the existing transaction+backup/restore safety net in `ApplyMigrationsAsync`). `ImportBatch.cs` gains `public SafeValue<DuplicateResolutionPolicy?> ConflictPolicy { get; init; }` (not a plain string — `Quotinator.Data` already has the right generic infrastructure for enum-backed columns via `SafeEnumHandler<TEnum>`, already used for `QuoteType`/`Genre`). Requires `RegisterEnumHandler<DuplicateResolutionPolicy>()` added to `QuotinatorDapperConfiguration.RegisterDomainHandlers()`. Populated at `CreateImportBatchAsync` from `seedBatch.Policy.ForQuotes`.

### 9. Add `System_ImportConflicts` table (Data-owned)
**Status:** ✅ Done — `ImportConflictMigrations.CreateImportConflictsTable` added as Data-owned migration 3; `SystemImportConflict`/`ImportConflictStatus` entity, `Sql.SystemImportConflicts` (mirrors `Sql.SystemAudit`), `ISystemImportConflictWriter`/`Reader` + SQLite implementations, `NoOpSystemImportConflictWriter` (test helper), DI registration in `Program.cs`. `QuotinatorDatabaseInitializer` now takes `ISystemImportConflictWriter` as a constructor dependency and writes one row per detected conflict (all policies, not just `Review`) via the new `LogImportConflictAsync` helper — `ExistingValue`/`IncomingValue` are JSON of the field map, `MergedFields` is a per-field `"ours"/"theirs"` JSON map populated only for merge policies. Fixed 5 call-site fallouts (`Program.cs` + 4 test files) from the new constructor parameter. `DataOwnedMigrations` count moved from 2 to 3 — `DataBaselineSql` and the Data-side/Engine-side schema-drift tests are not yet updated to match; expected red until step 10.

New `src/Quotinator.Data/Database/ImportConflictMigrations.cs` (mirrors `AuditMigrations.cs`), added directly to `DatabaseInitializer.DataOwnedMigrations` (tracked via `System_SchemaVersion`) — no rename-migration needed since the table is introduced with its final `System_`-prefixed name from the start. Columns: `Id` (long, autoincrement, `[Key]`), `BatchId` (string, loose reference — no FK, since Data doesn't know Engine's table names), `EntityType` (string, free text, e.g. `"Quote"`), `EntityId` (string, nullable), `ExistingValue`/`IncomingValue` (string, nullable, opaque JSON — Data never parses them; Engine produces and later diffs that content), `AppliedPolicy` (`SafeValue<DuplicateResolutionPolicy?>`), `Status` (string: `"resolved"`/`"pending"`), `MergedFields` (string, nullable, opaque JSON, populated only when `AppliedPolicy.Parsed` is `MergeOurs`/`MergeTheirs`), `DetectedAt` (DateTime), `ResolvedAt` (DateTime, nullable). New entity `src/Quotinator.Data/Entities/SystemImportConflict.cs`, `[Table("System_ImportConflicts")]`. New `Sql.SystemImportConflicts` nested class in `src/Quotinator.Data/Queries/Sql.cs` (mirrors `Sql.SystemAudit`). New `ISystemImportConflictWriter`/`ISystemImportConflictReader` in `src/Quotinator.Data/Repositories/` (mirrors `ISystemAuditWriter`/`ISystemAuditReader`), plus their implementations. `QuotinatorDatabaseInitializer`'s duplicate-detection loop writes one row per detected conflict (all five policies, not just `review`), including a merge row's `MergedFields` blob.

### 10. Update baseline schema and schema-drift tests
**Status:** ✅ Done — `QuotinatorMigrations.Baseline` gained `ImportBatches.ConflictPolicy` (appended last, matching the `ALTER TABLE ADD COLUMN` position, same `'skip'` default); `DatabaseInitializer.DataBaselineSql` gained `System_ImportConflicts`. Added a new `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportConflictsSchema` test (Data.Tests) mirroring the existing AuditEntries one — passes, confirming no drift. Existing `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` (Engine.Tests) both still pass unmodified, confirming the ImportBatches change is drift-free too. `DataSchemaVersion`/`System_SchemaVersion` row-count assertions (2→3, from Data's own migration count growing) are fixed together with the rest of the fallout in step 11.

Update `QuotinatorMigrations.Baseline` (`ImportBatches.ConflictPolicy`) and `DatabaseInitializer.DataBaselineSql` (`System_ImportConflicts`) in the same commit as their respective migrations, per CLAUDE.md's baseline policy. Re-run `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` and the equivalent Data-side schema-drift test for `System_ImportConflicts` — confirm no drift, don't assume.

### 11. Fix existing test fallout
**Status:** ✅ Done — full solution `dotnet test --configuration Release` is green (735 tests, 0 warnings, 0 errors). Fixed:
- `InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates`: `Skip` → `NewestWins` (misleading "(manifest default)" comment corrected — `AllFilesBatch()` uses `HardcodedDefault` directly, bypassing the bundled manifest's own `skip` override).
- `InitialiseAsync_AllSourceFiles_SeedsExpectedCounts`: `SourceCount` 478 → 479 — a genuine behaviour change from flipping the default (see step 7's fallout note), not a bug.
- All `SchemaVersion`/`DataSchemaVersion` numeric assertions across `DatabaseInitializerTests.cs`, `ImportBatchesTests.cs`, and `DatabaseInitializerOwnershipTests.cs` updated for the new migration counts (consumer 4→5, Data 2→3).
- `InitialiseAsync_PartialMigrationState_FailsSafelyAndRequiresExplicitReset`: **not** simply renumbered to `(4,5)` as originally guessed — investigated and found that `GetConsumerCurrentVersion` computes `MAX(Version)`, not row count, so leaving migration 5's row in place (deleting only 3 and 4) left the computed version at 5 and nothing replayed (test failed with "expected exception, none thrown"). Fixed by deleting `WHERE Version >= 3` instead, correctly dropping `MAX` back to 2 regardless of how many migrations exist above it.
- `InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally`: same `MAX`-vs-row-count issue — `WHERE Version = 4` changed to `WHERE Version >= 4`.
- `InitialiseAsync_PreSplitCombinedCounterDatabase_FailsSafelyAndRequiresExplicitReset`: combined pre-#143 legacy counter loop bound `1..6` (2 Data + 4 consumer, historical) → `1..8` (3 Data + 5 consumer, current).
- Added missing `SqlQueryGuardTests` aggregate-inventory entry and `RepositorySqlGuardTests` factory-method coverage for the new `Sql.SystemImportConflicts.SelectPaged`/`CountPaged` (mirroring existing `SystemAudit` coverage) — a real gap found by running the full suite, not just the targeted filter.

### 12. Add new test coverage
**Status:** ✅ Done — new `ConflictResolutionTests.cs` (Engine.Tests) exercises the real seeding pipeline end-to-end against two small fixture files sharing one quote Id: content-level `NewestWins` (surviving row matches the later file), `MergeOurs`/`MergeTheirs` (blank-field auto-fill + true-conflict tie-break, covering both a scalar field (`quoteText`) and an array field (`genres`)), `Review` (behaves like `Skip`), and a `HardcodedDefault == NewestWins` regression guard. `System_ImportConflicts` coverage: one row per conflict for all five policies with correct `Status` (`pending` only for `Review`), `MergedFields` populated only for the two merge policies (with correct per-field `"ours"`/`"theirs"` values) and null otherwise, and survival across `ResetAsync` (proven by re-seeding logging a second row on top of the surviving pre-Reset one, not by an impossible "stays at 1" expectation). `ConflictPolicyParser` extracted out of `Program.cs`'s local functions into `Quotinator.Data.Import` (with 17 new unit tests covering absent/garbage/all-five-values/case-insensitivity) — a real gap found while implementing this step, not merely following the plan text. `ImportBatches.ConflictPolicy` persistence test added to `ImportBatchesTests.cs`, plus the column added to its existing required-columns list. A gap in item 8's own coverage (manifest `duplicateResolution.default` omitted-key behaviour) was also found and closed with `DuplicateResolutionPolicyJsonConverterTests.ManifestPolicyDto_DefaultKeyOmitted_ResolvesToNewestWins`. Full solution `dotnet test` is green: 767 tests, 0 warnings, 0 errors.

**Explicitly out of scope for all steps above:** #45's per-run query param and endpoint; the actual review/resolve workflow UI.

---

## Scope changes

Reconciled 2026-07-04 — see comment on #64 for the full record:

- **`merge-ours`/`merge-theirs`/`review` policies and the new `System_ImportConflicts` table** were not in #64's original text. `review` had been drafted as invented scope conflated from #45's own `conflictStrategy: skip|overwrite|review` design; the plan doc previously proposed a `ConflictQueue` table with no GitHub-issue basis. These are now deliberately expanded and owned by #64, since #64 defines the durable conflict-resolution data model the rest of the milestone (and #45 specifically) builds on. Modeled on Git's own merge-conflict vocabulary (whole-side `ours`/`theirs`, recursive auto-merge with `-X ours`/`-X theirs`, manual resolution as the fallback).
- **Manifest-level `duplicateResolution` override and per-entity-type granularity** (`Quotes`/`Sources`/`Characters`/`People`/`Translations`) were built for #57/#61's cross-source dedup needs but never posted as a scope-change comment on any issue. Retroactively accepted and documented here.
- **Bundled `data/sources/manifest.json` file order** was reviewed and corrected (see below) as a directly related fix, even though file ordering is nominally #63's concern.
- #45's own `conflictStrategy`/`review`/`/resolve` design predates this reconciliation and overlaps in terminology — flagged via a comment on #45, not resolved here.

### Bundled file order fix

Field-population counts measured across the full files:

| File | Total | `date` | `character` | `genres` |
|---|---|---|---|---|
| `quotinator-curated.json` | 2 | 100% | 100% | 100% |
| `NikhilNamal17_popular-movie-quotes.json` | 732 | 99.9% | 0% | 0% |
| `vilaboim_movie-quotes.json` | 99 | 0% | 0% | 0% |

The manifest's `files` order was `curated → vilaboim → NikhilNamal17`, with the bundled manifest's explicit `"duplicateResolution": {"default": "skip"}` override (first-seen wins). This meant any quote appearing in both automated sources kept `vilaboim`'s bare, dateless version and discarded `NikhilNamal17`'s dated one — backwards from #63's own stated purpose. Reordered to `curated → NikhilNamal17 → vilaboim`: curated stays protected first, and the richer automated source now wins ties against the barer one.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `skip` keeps existing record; `newest-wins` overwrites with incoming (content-level) | Unit test | `ConflictResolutionTests.Review_BehavesIdenticallyToSkip` (Skip's behaviour), `ConflictResolutionTests.NewestWins_TrueConflictFields_SurvivingRowMatchesLaterFile` |
| 2 | ✅ | `merge-ours`/`merge-theirs` auto-fill blank fields from either side | Unit test | `FieldMergeResolverTests.Resolve_ExistingBlankIncomingSet_AutoFillsFromIncoming`, `Resolve_EmptyArrayFieldAutoFillsFromNonEmptySide`, `ConflictResolutionTests.MergeOurs_AutoFillsBlankFieldAndKeepsExistingOnTrueConflict`, `MergeTheirs_AutoFillsBlankFieldAndTakesIncomingOnTrueConflict` |
| 3 | ✅ | `merge-ours`/`merge-theirs` resolve a true field conflict (both sides non-empty, differing) per the correct tie-break direction, for both scalar and array fields | Unit test | `FieldMergeResolverTests.Resolve_TrueConflictScalarField_MergeOursKeepsExisting`/`MergeTheirsTakesIncoming`, `Resolve_TrueConflictArrayField_MergeOursKeepsExistingWholesaleNoUnion`/`MergeTheirsTakesIncomingWholesaleNoUnion`; content-level via `ConflictResolutionTests.MergeOurs_...`/`MergeTheirs_...` |
| 4 | ✅ | `review` behaves identically to `skip` today | Unit test | `ConflictResolutionTests.Review_BehavesIdenticallyToSkip` |
| 5 | ✅ | Default policy is `newest-wins` when nothing overrides it | Unit test | `ConflictResolutionTests.HardcodedDefault_IsNewestWins`, `ConflictPolicyParserTests.Parse_FallsBackToNewestWinsOnAbsentOrGarbage` |
| 6 | ✅ | Enum/config/schema/SQL vocabulary is consistent across all five values, no `overwrite` remaining | Live | `grep -rn "DuplicateResolutionPolicy\.Overwrite\|\"overwrite\"\|UpdateOnOverwrite" src/ tests/ data/ schemas/` — zero matches |
| 7 | ✅ | `Quotinator__DefaultConflictPolicy` read correctly; per-type keys still work nested | Unit test | `ConflictPolicyParserTests.Parse_FallsBackToNewestWinsOnAbsentOrGarbage`, `ParseNullable_ReturnsNullOnAbsentOrGarbage` |
| 8 | ✅ | Manifest `duplicateResolution.default`, when key present but value omitted, resolves to `newest-wins` | Unit test | `DuplicateResolutionPolicyJsonConverterTests.ManifestPolicyDto_DefaultKeyOmitted_ResolvesToNewestWins` |
| 9 | ✅ | All five wire strings round-trip correctly via the new JSON converter | Unit test | `DuplicateResolutionPolicyJsonConverterTests.Serialize_AllFiveValues_ProducesKebabCaseWireString`, `Deserialize_AllFiveWireStrings_ProducesCorrectEnumValue` |
| 10 | ✅ | `ImportBatches.ConflictPolicy` is enum-backed (`SafeValue<DuplicateResolutionPolicy?>`), backfills `'skip'` for pre-existing rows, and new batches persist their actual applied policy | Unit test | `ImportBatchesTests.Schema_ImportBatchesConflictPolicy_PersistsAppliedPolicy` (new-row persistence); backfill value covered by `Migration005_ImportBatchConflictPolicy`'s `DEFAULT 'skip'` clause plus `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema` (baseline/incremental DDL match, including the default) |
| 11 | ✅ | `System_ImportConflicts` logs every conflict (not just pending), correct `Status`, `MergedFields` populated only for merge resolutions | Unit test | `ConflictResolutionTests.SystemImportConflicts_LogsOneRowPerConflict_WithCorrectStatus` (all 5 policies), `SystemImportConflicts_MergedFields_PopulatedOnlyForMergePolicies`, `SystemImportConflicts_NonMergePolicy_MergedFieldsIsNull` |
| 12 | ✅ | `System_ImportConflicts` excluded from Reset, same as `System_AuditEntries` | Unit test | `ConflictResolutionTests.ResetAsync_PreservesExistingImportConflictRows` |
| 13 | ✅ | Baseline and incremental-replay schemas match after both new migrations | Unit test | `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` (Engine.Tests, `ImportBatches.ConflictPolicy`), `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportConflictsSchema` (Data.Tests, `System_ImportConflicts`) |
| 14 | N/A | Per-import override via `?conflictPolicy=` — blocked on #45 | N/A | Deferred, no seam added yet |
| 15 | ✅ | Bundled manifest file order lets richer sources win ties (`curated → NikhilNamal17 → vilaboim`) | Live | `data/sources/manifest.json` `files` order, verified against measured field-population counts above |
| 16 | ✅ | T2 — `docker build`/`docker run` smoke test; schema/counts match local dev exactly | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; container started cleanly (no errors, live source auto-refresh worked); `/api/v1/health`, `/api/v1/version` (v1.7.2, schema v5, 788 quotes/478 sources/2 characters/0 people — matches local), `/api/v1/quotes/random`, `/search?q=love`, `/search?q=Casablanca&field=source` all returned results; `field=author`/`character`/`type=person` correctly returned empty `NoResults`. Confirmed 2026-07-05 |
| 17 | ✅ | T1 — app starts in VS without error; migrations apply correctly; UI/API usable | Live | VS run: pre-migration backup taken, Data v2→v3 and App v4→v5 applied cleanly, schema v5 confirmed, stats match (788 quotes/478 sources/2 characters/0 people), `[UI] home page ready`, `/api/v1/quotes/random` and other endpoints returned 200, `POST /api/v1/admin/database/reseed` reproduced the same 45-duplicate breakdown with no unhandled exceptions. Confirmed 2026-07-05 |

**Full solution `dotnet test --configuration Release`: 767 tests, 0 warnings, 0 errors.** `dotnet build --configuration Release`: 0 warnings, 0 errors.

A real, benign discovery surfaced during T1 live testing was filed separately as [#147](https://github.com/DutchJaFO/Quotinator/issues/147) (Data Enrichment milestone) — 9 of the 45 detected duplicates are internal to the bundled `NikhilNamal17_popular-movie-quotes.json` file itself (upstream data has the same quote/source pair twice with differing year), not cross-file. Confirmed not a #64 bug — the conflict logging correctly reported it; no code change needed here.
