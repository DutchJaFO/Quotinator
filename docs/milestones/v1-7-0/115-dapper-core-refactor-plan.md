# Plan: #115 — Refactor: move all Dapper dependencies out of Quotinator.Core into Quotinator.Data

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/115  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Why this must come first

Issue #111 (flaky test) identified one parallel-execution race: concurrent `[ClassInitialize]` calls writing to Dapper's global type-handler map. The affected test files (`DatabaseInitializerTests`, `ImportBatchesTests`) are currently in `Quotinator.Core.Tests` but use Dapper directly — they belong in `Quotinator.Data.Tests`. Until they are moved, additional parallel-execution patterns in the correct test project context cannot be identified or verified. #111 cannot be closed until this refactor is complete and re-verified.

---

## Architectural intent

`Quotinator.Data` is being established as a **generic, reusable data-access and import/export infrastructure library** — not just Quotinator-specific glue. This means:

- Database initialisation, schema migration, and seeding are infrastructure concerns → `Quotinator.Data`
- Conflict resolution strategies for import pipelines (skip, overwrite) are generic, pre-built, and pluggable → `Quotinator.Data`
- Per-entity-type policy configuration (`ManifestPolicy`) is import infrastructure → `Quotinator.Data`
- Interfaces that abstract database-layer behaviour (`IDatabaseInitializer`, `IImportBatchRepository`) belong in the layer they abstract → `Quotinator.Data`
- Domain service interfaces (`IQuoteService`) and domain models (`Quote`, `QuoteResponse`) remain in `Quotinator.Core`
- When Core needs to manipulate data, it reaches for `Quotinator.Data` infrastructure rather than rolling its own

This boundary means `Quotinator.Core` depends on `Quotinator.Data` (already true), and `Quotinator.Data` never references `Quotinator.Core` (circular dependency avoided by keeping domain types in Core).

---

## What moves

### Production code — `Quotinator.Core` → `Quotinator.Data`

**Entities (Dapper.Contrib attributes):**

| File | New location |
|------|-------------|
| `Data/Entities/Character.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/CharacterTranslation.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/ImportBatch.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/Person.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/QuoteEntity.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/QuoteGenreEntity.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/QuoteTranslationEntity.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/Source.cs` | `Quotinator.Data/Entities/` |
| `Data/Entities/SourceTranslation.cs` | `Quotinator.Data/Entities/` |

**Infrastructure implementations:**

| File | New location |
|------|-------------|
| `Data/DatabaseInitializer.cs` | `Quotinator.Data/Database/` |
| `Data/Repositories/SqliteImportBatchRepository.cs` | `Quotinator.Data/Repositories/` |
| `Data/SqliteQuoteService.cs` | `Quotinator.Data/Services/` |
| `Data/TypeHandlers/DapperConfiguration.cs` | `Quotinator.Data/Helpers/` |

**Interfaces (belong in the layer they abstract):**

| File | New location | Reason |
|------|-------------|--------|
| `Data/IDatabaseInitializer.cs` | `Quotinator.Data/Database/` | Purely infrastructure; no domain meaning |
| `Data/Repositories/IImportBatchRepository.cs` | `Quotinator.Data/Repositories/` | Already extends `IRepository<T>` from Data |

**Import/export infrastructure (pluggable strategies):**

| File | New location | Reason |
|------|-------------|--------|
| `Data/DuplicateResolutionPolicy.cs` | `Quotinator.Data/Import/` | Generic conflict resolution strategy — reusable across any import pipeline |
| `Data/ManifestPolicy.cs` | `Quotinator.Data/Import/` | Per-entity-type policy configuration — generic import infrastructure |
| `Data/SeedDuplicateRecord.cs` | `Quotinator.Data/Import/` | Result model for `IDatabaseInitializer` — moves with the interface |
| `Data/SeedPreviewResult.cs` | `Quotinator.Data/Import/` | Result model for `IDatabaseInitializer` — moves with the interface |
| `Data/SeedBatch.cs` | `Quotinator.Data/Import/` | Import batch model used by seeding infrastructure |

**Enums:**

| File | New location | Reason |
|------|-------------|--------|
| `Data/Enums/ImportBatchType.cs` | `Quotinator.Data/Entities/` | Import infrastructure enum; `IImportBatchRepository` references it |

### What stays in `Quotinator.Core`

| File | Reason |
|------|--------|
| `Data/IQuoteService.cs` | Domain service contract |
| `Data/Sql.cs` | SQL query constants; no Dapper dependency |
| `Data/DataPaths.cs` | Borderline — database/backup path constants belong in Data, but `DataProtectionFolder` is an ASP.NET Core concern used in `Program.cs`. Evaluate at implementation time; may split or leave in Core. |
| `Data/Enums/QuoteType.cs` | Domain enum; used in API responses and service interfaces |
| `Data/Enums/Genre.cs` | Domain enum; used in API responses and service interfaces |

### Test code — `Quotinator.Core.Tests` → `Quotinator.Data.Tests`

| File | New location |
|------|-------------|
| `Data/DatabaseInitializerTests.cs` | `Quotinator.Data.Tests/Database/` |
| `Data/ImportBatchesTests.cs` | `Quotinator.Data.Tests/` |
| `Data/DapperSetupTests.cs` | `Quotinator.Data.Tests/` |
| `Data/SqliteQuoteServiceSearchTests.cs` | `Quotinator.Data.Tests/Services/` (uses Dapper directly) |

### Test code that stays in `Quotinator.Core.Tests`

| File | Reason |
|------|--------|
| `Data/SeedScriptIntegrityTests.cs` | No Dapper; tests JSON schema integrity |
| `Data/SourceDataIntegrityTests.cs` | No Dapper; tests JSON data integrity |

---

## Approach

1. **Move entity classes** — update namespaces to `Quotinator.Data.Entities`. Update all callers in `Quotinator.Core` that reference these types.
2. **Move infrastructure implementations** — `DatabaseInitializer`, `SqliteImportBatchRepository`, `SqliteQuoteService`; update `Quotinator.Api` DI registrations.
3. **Move interfaces** — `IDatabaseInitializer`, `IImportBatchRepository`; update all callers (Api DI registrations, any Core services that reference them).
4. **Move import/export infrastructure** — `DuplicateResolutionPolicy`, `ManifestPolicy`, `SeedDuplicateRecord`, `SeedPreviewResult`, `SeedBatch`, `ImportBatchType`; update all callers.
5. **Evaluate `DataPaths`** — decide at implementation time whether to split, move in full, or leave in Core.
6. **Move `DapperConfiguration`** to `Quotinator.Data/Helpers/`.
7. **Update `Quotinator.Data.Tests/MSTestSettings.cs`** — currently registers `GuidHandler` and `SafeDateHandler` manually; replace with a single `DapperConfiguration.Configure()` call once it is in the Data project.
8. **Remove Dapper NuGet references** (`Dapper`, `Dapper.Contrib`, `Microsoft.Data.Sqlite`) from `Quotinator.Core.csproj`.
9. **Move test files** to `Quotinator.Data.Tests`; update namespaces.
10. **Update `Quotinator.Core.Tests/MSTestSettings.cs`** — remove `DapperConfiguration.Configure()` call and its `using` once no Core.Tests file references Dapper.
11. **Re-verify parallel execution** in `Quotinator.Data.Tests` — check `[AssemblyInitialize]` compliance; check all `[ClassInitialize]` in moved test files; check `[TestCleanup]` for `ClearAllPools()` patterns.
12. **Update `Quotinator.slnx`** — reflect all moved files.
13. **Build and test** — 0 warnings, 0 errors; all tests pass.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `grep -rn "using Dapper" src/Quotinator.Core/` returns no matches | Live | Command returns empty |
| 2 | ❌ | Dapper not in `Quotinator.Core.csproj` package references | Live | `dotnet list package src/Quotinator.Core/Quotinator.Core.csproj` — no Dapper entry |
| 3 | ❌ | `grep -rn "using Dapper" tests/Quotinator.Core.Tests/` returns no matches | Live | Command returns empty |
| 4 | ❌ | All moved test files exist in `Quotinator.Data.Tests` with updated namespaces | Live | Files present; `dotnet build` succeeds |
| 5 | ❌ | `Quotinator.Data.Tests/MSTestSettings.cs` calls `DapperConfiguration.Configure()` from `[AssemblyInitialize]` | Live | File updated; no manual per-handler registration remains |
| 6 | ❌ | No `[ClassInitialize]` in moved test files writes to global state | Live | Grep for `ClassInitialize` in moved files; none call `SqlMapper.*` or `DapperConfiguration.*` |
| 7 | ❌ | Build clean | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 8 | ❌ | Full test suite green | Live | `dotnet test --configuration Release` — all tests pass |
| 9 | ❌ | `Quotinator.Data.Tests` stable under parallel execution | Live | 5 consecutive full-suite runs — all pass |
