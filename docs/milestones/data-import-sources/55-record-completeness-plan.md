# #55 — Record completeness flag

**Status:** In progress

**Tiers required:** T1, T2

**GitHub issue:** #55

**Depends on:** #64

**Connects to:** #56, #19, #48 (all different milestones or not yet started)

---

## Spec requirements (reconciled — see Scope changes)

The original issue predates #64 (conflict resolution) and #143 (migration ownership/baseline split); neither existed when it was written. Reconciled against the current codebase before planning:

1. New `IsComplete` column (`BIT NOT NULL DEFAULT 0`) on `Quotes`, `Sources`, `Characters`, `People`
2. New `NoValueKnown` column (`TEXT NOT NULL DEFAULT '[]'`, JSON array of field names) on all four tables
3. A brand-new row (first insert, whether via startup seeding or the `POST /api/v1/quotes/import` endpoint) always starts `IsComplete = false`, `NoValueKnown = []`, regardless of the source payload
4. **An existing row being rewritten by `newest-wins`/`merge-ours`/`merge-theirs`/`skip`/`review` (#64's conflict engine) must never reset `IsComplete`/`NoValueKnown`** — both columns are excluded entirely from the `UPDATE` path; only a genuinely new row gets the `false`/`[]` defaults
5. Enrichment providers skip records where `IsComplete = true` and skip individual fields listed in `NoValueKnown` (implementation deferred to #19)
6. Management UI actions ("Mark as complete", "Mark field as no value known") deferred to the Blazor import UI milestone (#11)
7. Stats/counts reporting deferred entirely to #48 (not yet built — see Scope changes)
8. **Database-only for now** — `IsComplete`/`NoValueKnown` are not added to `QuoteResponse` or any other public API response shape in this issue

---

## Steps

### 1. Schema migration
**Status:** ✅ Done — `Migration006_RecordCompleteness` added; `QuotinatorMigrations.Baseline` updated in the same commit; `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` both green

New `Migration006_RecordCompleteness` in `QuotinatorMigrations.cs` (next consumer migration number after #64's `Migration005_ImportBatchConflictPolicy`), following the established plain-`ALTER TABLE ADD COLUMN` pattern (no idempotency guard needed — the existing transaction+backup/restore safety net in `ApplyMigrationsAsync` covers a partial-failure case, per CLAUDE.md's migration policy):

```sql
ALTER TABLE Quotes     ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE Quotes     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
ALTER TABLE Sources    ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE Sources    ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
ALTER TABLE Characters ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE Characters ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
ALTER TABLE People     ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
ALTER TABLE People     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
```

Update `QuotinatorMigrations.Baseline` in the same commit (per CLAUDE.md's baseline-sync rule) — a fresh database must create these columns directly, not replay this migration. Confirm via `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues`, don't assume.

### 2. Update C# entity models
**Status:** ✅ Done

`QuoteEntity`, `Source`, `Character`, `Person` (all in `Quotinator.Engine.Entities`) each gained:
- `bool IsComplete { get; init; }`
- `IReadOnlyList<string> NoValueKnown { get; init; } = []`

No JSON equivalent existed on `SafeEnumHandler<TEnum>`/`DatabaseConfiguration` — added an open-generic `JsonHandler<T>` (`Quotinator.Data/Helpers/JsonHandler.cs`), registered for `IReadOnlyList<string>` via a new `DatabaseConfiguration.RegisterJsonHandler<T>()` helper (mirroring `RegisterEnumHandler<TEnum>`). Generalised (not left as a one-off list handler) at the user's request, so the same handler is ready to register for other JSON shapes later — e.g. a typed read of `System_ImportConflicts.MergedFields`/`ExistingValue`/`IncomingValue`, documented directly on `SystemImportConflict` for when #149 (manual conflict-review workflow) starts.

### 3. New-row defaults on every insert path
**Status:** ✅ Done

Both entity-creation paths need to write the defaults explicitly (or rely on the column `DEFAULT`, confirmed sufficient by a test — Dapper.Contrib's `InsertAsync` writes all mapped properties, so an explicit `false`/`[]` on the C# object achieves the same thing without depending on SQLite's own default):

- `QuoteSeedWriter.GetOrCreateSourceAsync`/`GetOrCreateCharacterAsync`/`GetOrCreatePersonAsync` (startup seeding, shared with #45's live import per the #45 extraction) — covered by the C# property defaults on `Source`/`Character`/`Person` themselves; Dapper.Contrib's `InsertAsync` writes all mapped properties, so no per-call-site change was needed
- The main quote insert in both `QuotinatorDatabaseInitializer.SeedIfEmptyInternalAsync` and `SqliteQuoteImportService.ImportAsync` — both call the single shared `Sql.Quotes.Insert` constant, which now lists `IsComplete`/`NoValueKnown` with explicit `0`/`'[]'` literals

### 4. Existing-row updates never touch these columns
**Status:** ✅ Done

`Sql.Quotes.UpdateOnNewestWins` does not reference `IsComplete`/`NoValueKnown` in its `SET` list (confirmed by inspection — unchanged by this issue). Source/Character/Person's `GetOrCreate*` "found existing" branches never issue an `UPDATE` at all — they only return the existing Id — so there was nothing to guard there.

**New tests added** (both pipelines that can hit an existing row):
- `ConflictResolutionTests.UpdateOnNewestWins_NeverResetsIsCompleteOrNoValueKnown` — direct regression guard on the shared production `Sql.Quotes.UpdateOnNewestWins` statement itself
- `QuoteImportServiceTests.ImportAsync_ExistingRowMarkedComplete_SurvivesReimportUnchanged` (`NewestWins`/`MergeOurs`/`MergeTheirs`) — same guarantee via the live import endpoint

### 5. Stats/counts
**Status:** N/A — deferred entirely to #48 (see Scope changes)

### 6. Enrichment hooks
**Status:** N/A — deferred to #19 (different milestone, not started)

### 7. Blazor UI hooks
**Status:** N/A — deferred to #11 (Blazor: Import UI milestone)

### 8. Tests
**Status:** ✅ Done — see step 4 for the two correctness-critical cases; also added: schema migration + baseline drift (existing `Baseline_And_IncrementalReplay_*` tests, now covering the two new columns automatically since they dump all columns per table), `JsonStringListHandler` round-trip (`Quotinator.Data.Tests/Helpers/DapperSetupTests.cs`), and default-values-on-new-insert (`ConflictResolutionTests.Seed_FreshQuote_DefaultsIsCompleteFalseAndNoValueKnownEmpty`, `QuoteImportServiceTests.ImportAsync_FreshDatabase_DefaultsIsCompleteFalseAndNoValueKnownEmpty`).

Adding `Migration006` also broke three pre-existing tests that hardcoded `SchemaVersion == 5` (`ImportBatchesTests.Schema_MigrationVersion_IsBumped`, and two assertions each in `DatabaseInitializerTests` for the Reset-after-mismatch and pre-split-counter scenarios) — updated to `6`. One test, `DatabaseInitializerTests.InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally`, had a latent design flaw exposed by this migration: it built its "version 3" starting state by deleting `System_ConsumerSchemaVersion` rows on an already-fully-migrated database file rather than genuinely replaying only migrations 1-3, which happened to work only because migration 4 rebuilds the `ImportBatches` table from scratch (silently discarding migration 5's `ConflictPolicy` column before migration 5 re-added it). Migration 6's `ALTER TABLE ADD COLUMN` on `Quotes`/`Sources`/`Characters`/`People` — tables nothing ever rebuilds — has no such masking, so replaying it a second time threw `duplicate column name: IsComplete`. Rewrote the test to build its initial database with only `QuotinatorMigrations.All.Take(3)` actually applied, so it exercises a genuine version-3 replay instead of a masked schema/version mismatch.

---

## Scope changes

Reconciled 2026-07-05, before implementation — pending a comment on #55 recording the same:

- **Update-path preservation of `IsComplete`/`NoValueKnown` is a new, explicit requirement** not present in the original issue text — added because #64's conflict engine (which didn't exist when #55 was written) rewrites existing rows on every reseed/reimport, and silently resetting a human's completed review on every reseed would defeat the entire point of the feature. "Import always sets isComplete: false" is now understood to apply only to a row's first insert, never to an update of an existing row.
- **Stats/counts reporting is deferred to #48** (stats endpoint), which is still open and unstarted — #55 ships schema and model changes only, matching how enrichment (#19) and the Blazor UI (v3/#11) are already deferred in the original issue text.
- **No public API exposure in this issue** — `IsComplete`/`NoValueKnown` do not appear in `QuoteResponse` or any other response DTO; they are database-only until a management API/UI actually needs to read or write them.
- **`NoValueKnown` ships on all four tables**, including `Characters` (whose only field, `Name`, is required and has no candidate for "no value known" today) — kept for consistency and to avoid a later schema change if `Characters` ever gains a nullable field (e.g. a nickname/alias).

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `IsComplete`/`NoValueKnown` columns added to all four tables, baseline matches incremental replay | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`/`...AcceptSameCheckConstraintValues` |
| 2 | ✅ | A brand-new row defaults to `IsComplete = false`, `NoValueKnown = []` | Unit test | `ConflictResolutionTests.Seed_FreshQuote_DefaultsIsCompleteFalseAndNoValueKnownEmpty`, `QuoteImportServiceTests.ImportAsync_FreshDatabase_DefaultsIsCompleteFalseAndNoValueKnownEmpty` |
| 3 | ✅ | An existing row's `IsComplete`/`NoValueKnown` survive `newest-wins`/`merge-ours`/`merge-theirs` via the shared production UPDATE statement | Unit test | `ConflictResolutionTests.UpdateOnNewestWins_NeverResetsIsCompleteOrNoValueKnown` |
| 4 | ✅ | Same guarantee via the live `POST /api/v1/quotes/import` endpoint | Unit test | `QuoteImportServiceTests.ImportAsync_ExistingRowMarkedComplete_SurvivesReimportUnchanged` (`NewestWins`/`MergeOurs`/`MergeTheirs`) |
| 5 | ✅ | `NoValueKnown` round-trips correctly between `string[]`/`IReadOnlyList<string>` and its JSON TEXT column | Unit test | `DapperSetupTests.JsonHandler_RegisteredByAssemblySetup_RoundTripsListThroughJsonColumn`, `...EmptyList_RoundTripsAsEmptyJsonArray`, `...RegisteredForDictionaryShape_RoundTripsThroughJsonColumn`, `...NullColumnValue_RoundTripsAsNull` |
| 6 | N/A | Stats endpoint reports completeness counts | N/A | Deferred to #48 |
| 7 | N/A | Enrichment providers skip complete/no-value-known fields | N/A | Deferred to #19 |
| 8 | N/A | Management UI actions | N/A | Deferred to #11 |
| 9 | ⬜ | T1 — app starts in VS without error; migration applies cleanly | Live | Not yet run — requires user to start the app in Visual Studio |
| 10 | ✅ | T2 — Docker smoke test | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; container started, log showed `schema created at baseline (data v3, app v6)`; `/api/v1/health`, `/api/v1/version` (`schemaVersion:6`), `/api/v1/quotes/random`, `/api/v1/quotes/search?q=love` all returned expected output |
