# Issue #115 — Refactor: move all Dapper dependencies out of Quotinator.Core into Quotinator.Data

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

`Quotinator.Core` has a direct dependency on Dapper and Dapper.Contrib, violating the intended architecture. `Quotinator.Data` is the designated home for all SQLite/Dapper infrastructure. Core should contain only models, interfaces, and pure service logic with no ORM dependency.

## Scope

### Production code to move to `Quotinator.Data`

| Source (Quotinator.Core) | Destination (Quotinator.Data) |
|--------------------------|-------------------------------|
| `Data/DatabaseInitializer.cs` | `Database/DatabaseInitializer.cs` |
| `Data/Entities/Character.cs` | `Entities/Character.cs` |
| `Data/Entities/CharacterTranslation.cs` | `Entities/CharacterTranslation.cs` |
| `Data/Entities/ImportBatch.cs` | `Entities/ImportBatch.cs` |
| `Data/Entities/Person.cs` | `Entities/Person.cs` |
| `Data/Entities/QuoteEntity.cs` | `Entities/QuoteEntity.cs` |
| `Data/Entities/QuoteGenreEntity.cs` | `Entities/QuoteGenreEntity.cs` |
| `Data/Entities/QuoteTranslationEntity.cs` | `Entities/QuoteTranslationEntity.cs` |
| `Data/Entities/Source.cs` | `Entities/Source.cs` |
| `Data/Entities/SourceTranslation.cs` | `Entities/SourceTranslation.cs` |
| `Data/Repositories/SqliteImportBatchRepository.cs` | `Repositories/SqliteImportBatchRepository.cs` |
| `Data/SqliteQuoteService.cs` | `Services/SqliteQuoteService.cs` |
| `Data/TypeHandlers/DapperConfiguration.cs` | `TypeHandlers/DapperConfiguration.cs` |

### Test code to move to `Quotinator.Data.Tests`

| Source (Quotinator.Core.Tests) | Destination (Quotinator.Data.Tests) |
|--------------------------------|--------------------------------------|
| `Data/DatabaseInitializerTests.cs` | `Database/DatabaseInitializerTests.cs` |
| `Data/ImportBatchesTests.cs` | `Repositories/ImportBatchesTests.cs` |
| `Data/DapperSetupTests.cs` | `TypeHandlers/DapperSetupTests.cs` |
| `MSTestSettings.cs` (Dapper call only) | Remove `DapperConfiguration.Configure()` call; move if needed |

### Aftermath

- Remove Dapper NuGet reference from `Quotinator.Core.csproj`
- Update all callers in `Quotinator.Core` that reference moved entity namespaces
- `SqliteQuoteService` implements `IQuoteService` (stays in Core) — implementation moves to `Quotinator.Data`, interface stays in Core
- `Quotinator.Data.Tests` receives the moved test files and the `[AssemblyInitialize]` for `DapperConfiguration.Configure()`
- Verify `Quotinator.Data` project reference is present in consuming projects

---

## Key considerations

- Moving entity classes changes their namespace — all callers in `Quotinator.Core` (service, repository) that reference these types need updating.
- After the move, run `grep -rn "using Dapper" src/Quotinator.Core/` — must return zero matches.
- The `SqliteQuoteService` in the new test file (`SqliteQuoteServiceSearchTests.cs`, added in #109) references `Quotinator.Core.Data` namespaces — must be updated as part of this move.
- All SQL stays in `Sql.cs` (Core); the services in Data reference it via the cross-project dependency.
- This is a prerequisite for #111 — moving the test files may surface additional parallel execution issues currently masked.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | No `using Dapper` in Quotinator.Core production code | Live | `grep -rn "using Dapper" src/Quotinator.Core/` returns 0 matches |
| 2 | ⬜ | No `using Dapper` in Quotinator.Core.Tests | Live | `grep -rn "using Dapper" tests/Quotinator.Core.Tests/` returns 0 matches |
| 3 | ⬜ | Dapper not in `Quotinator.Core.csproj` package references | Code review | `Quotinator.Core.csproj` — no `PackageReference Include="Dapper*"` |
| 4 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 5 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` — all pass |
| 6 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
