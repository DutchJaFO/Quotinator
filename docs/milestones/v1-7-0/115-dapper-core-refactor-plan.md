# Plan: #115 — Refactor: move all Dapper dependencies out of Quotinator.Core into Quotinator.Data

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/115  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Why this must come first

Issue #111 (flaky test) identified one parallel-execution race: concurrent `[ClassInitialize]` calls writing to Dapper's global type-handler map. The affected test files (`DatabaseInitializerTests`, `ImportBatchesTests`) are currently in `Quotinator.Core.Tests` but use Dapper directly — they belong in `Quotinator.Data.Tests`. Until they are moved, additional parallel-execution patterns in the correct test project context cannot be identified or verified. #111 cannot be closed until this refactor is complete and re-verified.

---

## What moves

### Production code — `Quotinator.Core` → `Quotinator.Data`

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
| `Data/DatabaseInitializer.cs` | `Quotinator.Data/Database/` (no conflict — this folder does not yet exist in Quotinator.Data) |
| `Data/Repositories/SqliteImportBatchRepository.cs` | `Quotinator.Data/Repositories/` |
| `Data/SqliteQuoteService.cs` | `Quotinator.Data/Services/` |
| `Data/TypeHandlers/DapperConfiguration.cs` | `Quotinator.Data/Helpers/` |

### What stays in `Quotinator.Core`

The following files in `Quotinator.Core/Data/` do **not** use Dapper and stay where they are:

- `Data/IDatabaseInitializer.cs` — interface; contract belongs in Core
- `Data/Repositories/IImportBatchRepository.cs` — interface; contract belongs in Core
- `Data/Sql.cs` — SQL string constants; no Dapper dependency
- `Data/DataPaths.cs`, `Data/ManifestPolicy.cs`, `Data/DuplicateResolutionPolicy.cs`, `Data/SeedBatch.cs`, `Data/SeedDuplicateRecord.cs`, `Data/SeedPreviewResult.cs` — no Dapper dependency
- `Data/Enums/` — no Dapper dependency

### Test code — `Quotinator.Core.Tests` → `Quotinator.Data.Tests`

| File | Action |
|------|--------|
| `Data/DatabaseInitializerTests.cs` | Move to `Quotinator.Data.Tests/Database/` |
| `Data/ImportBatchesTests.cs` | Move to `Quotinator.Data.Tests/` |
| `Data/DapperSetupTests.cs` | Move to `Quotinator.Data.Tests/` |
| `Data/SqliteQuoteServiceSearchTests.cs` | Move to `Quotinator.Data.Tests/Services/` (uses Dapper directly) |

### Test code that stays in `Quotinator.Core.Tests`

- `Data/SeedScriptIntegrityTests.cs` — no Dapper; tests JSON schema integrity
- `Data/SourceDataIntegrityTests.cs` — no Dapper; tests JSON data integrity

---

## Approach

1. **Move entity classes** — update namespaces to `Quotinator.Data.Entities`. Update all callers in `Quotinator.Core` (services, interfaces) that reference these types.
2. **Move `DatabaseInitializer`, `SqliteImportBatchRepository`, `SqliteQuoteService`** — update namespaces; update `Quotinator.Api` DI registrations; verify the interfaces (`IDatabaseInitializer`, `IImportBatchRepository`, `IQuoteService`) stay in `Quotinator.Core`.
3. **Move `DapperConfiguration`** to `Quotinator.Data/Helpers/`.
4. **Update `Quotinator.Data.Tests/MSTestSettings.cs`** — currently registers `GuidHandler` and `SafeDateHandler` manually; replace with a single `DapperConfiguration.Configure()` call once it is in the Data project.
5. **Remove Dapper NuGet references** (`Dapper`, `Dapper.Contrib`, `Microsoft.Data.Sqlite`) from `Quotinator.Core.csproj`.
6. **Move test files** to `Quotinator.Data.Tests`; update namespaces.
7. **Update `Quotinator.Core.Tests/MSTestSettings.cs`** — remove `DapperConfiguration.Configure()` call and its `using` once no Core.Tests file references Dapper.
8. **Re-verify parallel execution** in `Quotinator.Data.Tests` — check `MSTestSettings.cs` for `[AssemblyInitialize]` compliance; check all `[ClassInitialize]` methods in moved test files; check all `[TestCleanup]` for `ClearAllPools()` usage patterns.
9. **Update `Quotinator.slnx`** — reflect all moved files.
10. **Build and test** — 0 warnings, 0 errors; all tests pass.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `grep -rn "using Dapper" src/Quotinator.Core/` returns no matches | Live | Command returns empty |
| 2 | ❌ | Dapper not in `Quotinator.Core.csproj` package references | Live | `dotnet list package src/Quotinator.Core/Quotinator.Core.csproj` — no Dapper entry |
| 3 | ❌ | `grep -rn "using Dapper" tests/Quotinator.Core.Tests/` returns no matches | Live | Command returns empty |
| 4 | ❌ | All moved test files exist in `Quotinator.Data.Tests` with updated namespaces | Live | Files present; `dotnet build` succeeds |
| 5 | ❌ | `Quotinator.Data.Tests/MSTestSettings.cs` calls `DapperConfiguration.Configure()` from `[AssemblyInitialize]` | Live | File updated; no per-class or manual handler registration remains |
| 6 | ❌ | No `[ClassInitialize]` in moved test files writes to global state | Live | Grep for `ClassInitialize` in moved files; verify none call `SqlMapper.*` or `DapperConfiguration.*` |
| 7 | ❌ | Build clean | Live | `dotnet build --configuration Release` — 0 warnings, 0 errors |
| 8 | ❌ | Full test suite green | Live | `dotnet test --configuration Release` — all tests pass |
| 9 | ❌ | `Quotinator.Data.Tests` stable under parallel execution | Live | 5 consecutive full-suite runs — all pass |
