# #58 — ImportBatches schema

**Status:** Issue closed ✅ — post-closure regression fix pending release  
**GitHub issue:** #58  
**Tiers required:** T1, T2  
**Depends on:** #71 (generic repository pattern)  
**Unblocks:** #56, #57 (Problem 4), #59, #45 (batch row), #64 (policy recording), #67, #68, #69

**Note (2026-06-30):** items 7–8 in the verification table cover a regression found *after* #58 was closed (`Type`/`Url` hardcoded wrong on `ImportBatch` rows). The fix is committed on `feature/data-import-sources` with T1+T2 verified, but has not shipped in any release yet. This is "pending release", not "done" — no further closing action applies since #58 is already closed; this is tracked here purely as a verification record for the fix itself.

**Note (2026-07-02):** a second post-closure correction — `ImportBatchType.System` no longer applies to bundled quote content. During the #141 naming-convention work (`System_`-prefixed database tables), the user pointed out that reusing "System" to describe *quote content provenance* (bundled files with no URL, e.g. `quotinator-curated.json`) was a conflation with the newly-established meaning: "System" now specifically means the database's own `System_`-prefixed infrastructure tables (`System_SchemaVersion`, `System_AuditEntries`), not quote content. `quotinator-curated.json` is reclassified `Seed` — the `Url` column (null vs set) already carries the externally-sourced-vs-internally-authored distinction. `ImportBatchType.System` itself is **kept in the enum**, per explicit user correction, reserved for a future import batch that populates a `System_`-prefixed table specifically — no current source produces it, since nothing today seeds a `System_` table via the import batch mechanism. `DetermineType` now returns `Seed` for any bundled file regardless of URL presence; only `SeedBatchOrigin.UserImports` still produces a distinct type (`UserSeed`). Item 7's "System otherwise" and item 8's "curated→`System`/`NULL`" below are historically accurate for what was true at the time but are now superseded — see #62's plan doc and #141's plan doc for the full correction.

---

## Spec requirements

1. New `ImportBatches` table with columns: `Id` (UUID, PK), `Name` (TEXT NOT NULL), `Type` (TEXT NOT NULL — `seed` | `import` | `user-seed`; the CHECK constraint from migration 3 still permits the historical `system` value for compatibility, but it is no longer produced — see item 2), `Url` (TEXT, nullable), `ImportedAt` (TEXT NOT NULL, ISO 8601 UTC), `ImportedBy` (TEXT, nullable), `RecordCount` (INT NOT NULL DEFAULT 0)
2. Type values (as amended 2026-07-02 — see the note above): `seed` (any bundled file, whether externally sourced with a `Url`/`github` object or internally authored with none — the `Url` column itself carries that distinction), `user-seed` (file scanned from the user's imports folder at startup, regardless of `Url`), `import` (via bulk import endpoint, `ImportedBy` = user UUID). **`user-seed` added 2026-07-01 (#62) — see that plan doc for the full rationale and the `SeedBatchOrigin` mechanism that determines it.** `system` originally meant "fixed/predetermined bundled data with no `Url`" — that meaning was removed 2026-07-02 (superseded by `Sql.Schema.GetUserTables`'s `System_`-prefixed table naming convention, a database-table-level concept, not a quote-content-provenance one — see #141's plan doc). The `system` enum value itself is kept, reserved for a future import batch that populates a `System_`-prefixed table specifically; nothing produces it today.
3. Nullable `ImportBatchId TEXT REFERENCES ImportBatches(Id)` column on `Quotes`, `Characters`, `Sources`, `People`
4. Pre-seeded batch rows for vilaboim and NikhilNamal17 (Type = `seed`); existing records stay `NULL` — provenance not captured retroactively
5. Seeder creates one `ImportBatch` row per source file before seeding; writes `ImportBatchId` on all inserts for that file
6. Schema migration version bump

---

## Implementation steps

1. [x] Bump schema version in `DatabaseInitializer`
2. [x] Add `ImportBatches` table to schema DDL
3. [x] Add nullable `ImportBatchId TEXT REFERENCES ImportBatches(Id)` column to `Quotes`, `Characters`, `Sources`, `People`
4. [x] Add `ImportBatch` C# record in `Quotinator.Core`
5. [x] Add `IImportBatchRepository` (extends `IRepository<ImportBatch>`) in `Quotinator.Core`
6. [x] Add `SqliteImportBatchRepository` (extends `SqliteRepository<ImportBatch>`) in `Quotinator.Core`
7. [x] Register `IImportBatchRepository` → `SqliteImportBatchRepository` in DI (switch `DatabaseInitializer` to DI-managed registration so the repository can be injected)
8. [x] Update seeder to create one `ImportBatch` row per source file and pass `ImportBatchId` to all insert calls
9. [x] Insert pre-seed rows for vilaboim and NikhilNamal17 on migration (existing records stay `NULL`)
10. [x] Integration tests (see verification table)

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ImportBatches` table created with correct columns | Unit test | `ImportBatchesTests.Schema_ImportBatchesTable_HasAllRequiredColumns` |
| 2 | ✅ | Nullable `ImportBatchId` FK on all four entity tables | Unit test | `ImportBatchesTests.Schema_EntityTables_HaveNullableImportBatchIdFK` |
| 3 | ✅ | Pre-seed rows for vilaboim and NikhilNamal17 present after migration | Unit test | `ImportBatchesTests.Seeding_PreSeedBatches_ExistAfterMigration` |
| 4 | ✅ | Seeder creates one `ImportBatch` row per source file | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes` |
| 5 | ✅ | Existing records retain `NULL` `ImportBatchId` after migration | Unit test | `ImportBatchesTests.Migration_ExistingRecords_HaveNullImportBatchId` |
| 6 | ✅ | Schema migration version bumped | Unit test | `ImportBatchesTests.Schema_MigrationVersion_IsBumped` |
| 7 | ✅ | Seeder sets correct `Type`/`Url` per file (Seed when manifest has a `url`, System otherwise) | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes` |
| 8 | ✅ | Fresh seed in Visual Studio produces correct `Type`/`Url` per row | Live | Start app in VS with an empty database; inspect `ImportBatches` table; confirm curated→`System`/`NULL`, vilaboim/NikhilNamal17→`Seed`/GitHub URL. Confirmed 2026-06-30 |
| 9 | ✅ | Docker build/publish includes updated `manifest.json`; seeding behaves identically in container | Live | `docker build -f docker/Dockerfile -t quotinator:local .`; run; confirm container log shows same counts as T1 (788/478/2/0); `/api/v1/health`, `/api/v1/version`, `/api/v1/quotes/random` return expected output. Confirmed 2026-06-30 |
| 10 | ✅ | `ImportBatchType.System` no longer produced for bundled quote content; any bundled file (with or without a URL) is classified `Seed`; the enum value itself is kept, reserved for future `System_`-table content imports | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes` (updated 2026-07-02 — both the curated file and the vilaboim file now assert `Type = "Seed"`) |
| 11 | ⬜ | `quotinator-curated.json` is classified `Seed`/`NULL` (not `System`) in a real running app | Not yet re-verified | The user's 2026-07-02 screenshot showing `quotinator-curated.json` → `System` (highlighted) is what prompted this fix — it predates the code change and does not verify it. Needs a fresh reseed/reset against the fixed code to confirm `Seed`/`NULL`. |

---

## Repository design decision (resolved)

**Decision: Option B — dedicated `IImportBatchRepository`.**

Reason: downstream issues (#59 soft-reset by batch, #60 Blazor batches page) require listing all batches and filtering by type. These are not on the base `IRepository<T>` interface. Using Option A would force raw Dapper calls outside the repository wherever these queries are needed, violating the string centralisation policy.

```csharp
// Quotinator.Core
public interface IImportBatchRepository : IRepository<ImportBatch>
{
    Task<IReadOnlyList<ImportBatch>> GetAllAsync(IUnitOfWork? unitOfWork = null);
    Task<IReadOnlyList<ImportBatch>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null);
    Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null);
}

public sealed class SqliteImportBatchRepository : SqliteRepository<ImportBatch>, IImportBatchRepository
{
    // additional methods via Dapper
}
```

Both the interface and implementation live in `Quotinator.Core` — not `Quotinator.Data`. `Quotinator.Data` has no reference to `Quotinator.Core`; putting a concrete entity repository there would create a circular dependency.

`DatabaseInitializer` switches from `new` (in `Program.cs`) to full DI registration so `IImportBatchRepository` can be injected via constructor — per the DI policy in `CLAUDE.md`.

---

## Notes

`RecordCount` is denormalised for display performance; updated after each import run and after targeted resets.

`ImportBatches` is excluded from any export endpoint — it is instance-specific provenance data.

`NULL` `ImportBatchId` means the record predates provenance tracking or was created via the management UI with no associated import.
