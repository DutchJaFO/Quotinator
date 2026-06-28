# Plan: #121 — Refactor: remove Dapper dependency from SqliteQuoteService

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/121  
**Milestone:** v1.7.0  
**Status:** 🟡 Code complete — pending release

---

## Context

Issue #115 moved all Dapper infrastructure out of `Quotinator.Core` into `Quotinator.Data`, but `SqliteQuoteService` could not move because it bridges Core domain types (`IQuoteService`, `QuoteResponse`, `FilteredQuoteResult`) and Dapper infrastructure. Moving it to `Quotinator.Data` was blocked by a Core↔Data circular dependency.

The full cross-check for this issue (see session 2026-06-28) revealed that the root cause is a missing layer: there is no project that can legitimately reference both Core's domain interfaces and Data's generic tools. Solving only the symptom (moving `SqliteQuoteService`) without creating that layer would leave the architecture incomplete.

---

## Architectural decision — introduce `Quotinator.Engine`

`Quotinator.Data` is a **generic, domain-agnostic data-access library** — it must not know about Quotes, Persons, Sources, or any Quotinator domain type. This rules out putting Quotinator-specific implementations there.

`Quotinator.Core` must have no database knowledge at all — no Dapper, no SQLite, no SQL strings. This is the engine-independence guarantee: if the database engine changes, Core and Api are untouched.

The solution is a new project, **`Quotinator.Engine`**, that:
- References `Quotinator.Core` (to implement its domain interfaces)
- References `Quotinator.Data` (to use its generic repository and infrastructure tools)
- Is referenced only by `Quotinator.Api` (for DI registration)
- Is never referenced by Core or Data

`Quotinator.Engine` is the SQLite-backed implementation of the Quotinator domain. If the database engine were replaced, the Engine project would be the only thing that changes.

### Dependency graph after this issue

```
Quotinator.Constants  ←  Quotinator.Core  ←  Quotinator.Engine  ←  Quotinator.Api
                                                      ↑
                          Quotinator.Data  ←──────────┘
```

Core has no reference to Data. Engine bridges both. Api wires everything.

---

## What changes per project

### `Quotinator.Core` — gains domain types, loses all DB knowledge

**Gains (moved from `Quotinator.Data`):**

| Type | From | Namespace in Core |
|------|------|-------------------|
| `Genre` enum | `Quotinator.Data.Entities` | `Quotinator.Core.Models` |
| `QuoteType` enum | `Quotinator.Data.Entities` | `Quotinator.Core.Models` |
| `SourceQuote` | `Quotinator.Data.Import` | `Quotinator.Core.Import` |
| `SourceQuoteTranslation` | `Quotinator.Data.Import` | `Quotinator.Core.Import` |

**`Core.csproj` removes:**
- `<PackageReference Include="Dapper" />`
- `<PackageReference Include="Microsoft.Data.Sqlite" />`
- `<ProjectReference Include="..\Quotinator.Data\..." />`

**`Core/Data/` folder** — `SqliteQuoteService.cs` and `QuotinatorMigrations.cs` move to Engine; folder is deleted.

---

### `Quotinator.Data` — gains abstract base classes, loses nothing generic

**New abstract base: `DatabaseConfiguration`**

Replaces the current concrete `DapperConfiguration`. Registers generic handlers internally and exposes a protected template method for domain-specific registration:

```csharp
public abstract class DatabaseConfiguration
{
    public void Configure()
    {
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
        RegisterDomainHandlers();
    }

    protected virtual void RegisterDomainHandlers() { }

    // Engine calls this — no Dapper API surface exposed outside Data
    protected void RegisterEnumHandler<TEnum>() where TEnum : struct, Enum
        => SqlMapper.AddTypeHandler(new SafeEnumHandler<TEnum>());
}
```

**Refactored `DatabaseInitializer`**

The existing `DatabaseInitializer` contains both generic migration infrastructure and Quotinator-specific seeding. Split into:
- `DatabaseInitializer` (abstract base, stays in Data): handles schema versioning and migration execution only. Seeding moved to protected virtual hook.
- `QuotinatorDatabaseInitializer` (in Engine): extends the base; provides migrations and domain seeding via `IRepository<T>` interfaces — no Dapper calls.

**`Quotinator.Data` does not gain a Core project reference.** It remains fully domain-agnostic.

---

### `Quotinator.Engine` (new project — `src/Quotinator.Engine/`)

**Project references:** `Quotinator.Core`, `Quotinator.Data`  
**NuGet packages:** `Dapper`, `Dapper.Contrib`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Logging.Abstractions`  
**XML doc:** required (CS1591 active, 0-warnings policy)

**Contents:**

| File | Description |
|------|-------------|
| `QuotinatorMigrations.cs` | Quotinator DDL migrations (moved from Core) |
| `QuotinatorDatabaseInitializer.cs` | Extends `DatabaseInitializer`; provides `QuotinatorMigrations.All`; seeds via `IRepository<T>` |
| `QuotinatorDapperConfiguration.cs` | Extends `DatabaseConfiguration`; overrides `RegisterDomainHandlers` to call `RegisterEnumHandler<Genre>()` and `RegisterEnumHandler<QuoteType>()` |
| `SqliteQuoteService.cs` | Implements `IQuoteService`; uses specialized repository class(es) for complex multi-join queries; all SQL and Dapper hidden here |
| `Repositories/` | New specialized repository class(es) for the multi-join quote query pattern (extends `JoinQueryRepository<TResult>` or adds a new pattern if needed) |

**Engine-level SQL** (Quotinator-domain SQL strings, SQLite syntax): lives here alongside the classes that use them.

---

### `Quotinator.Api` — wiring update only

- Adds `<ProjectReference>` to `Quotinator.Engine`
- `Program.cs`: registers `QuotinatorDapperConfiguration`, `QuotinatorDatabaseInitializer`, and `SqliteQuoteService` (from Engine) instead of from Core
- No SQL, no Dapper, no SQLite in Api

---

### Callers of moved types — namespace updates required

Types that move must have their `using` directives updated in all callers:

| Type | Old namespace | New namespace | Callers |
|------|--------------|---------------|---------|
| `Genre` | `Quotinator.Data.Entities` | `Quotinator.Core.Models` | `SqliteQuoteService`, `DapperConfiguration`, DB entities in Data, `InputValidation` usages |
| `QuoteType` | `Quotinator.Data.Entities` | `Quotinator.Core.Models` | Same |
| `SourceQuote` | `Quotinator.Data.Import` | `Quotinator.Core.Import` | `QuoteService.cs` in Core, seeder in Engine |
| `SourceQuoteTranslation` | `Quotinator.Data.Import` | `Quotinator.Core.Import` | Same |

`Quotinator.Data.Entities` classes (`Source`, `QuoteGenreEntity`, etc.) that use `SafeValue<Genre?>` and `SafeValue<QuoteType?>` will gain a `using Quotinator.Core.Models;` import but no project reference change is needed — Data does not reference Core; the type parameter is passed in from callers (Engine) that do.

Wait — `SafeValue<Genre?>` in Data entity classes IS a compile-time reference to `Genre`. Data entities cannot use `Genre` if Data does not reference Core. **Resolution:** DB entity classes that currently use `SafeValue<Genre?>` and `SafeValue<QuoteType?>` are Quotinator-domain entities — they belong in Engine, not Data. Move `Character`, `CharacterTranslation`, `ImportBatch`, `Person`, `QuoteEntity`, `QuoteGenreEntity`, `QuoteTranslationEntity`, `Source`, `SourceTranslation` to `Quotinator.Engine/Entities/`.

`AuditEntry` is generic (used by `AuditWriter` in Data) — it stays in Data.

---

### Revised entity placement

| Entity | Current location | New location | Reason |
|--------|-----------------|--------------|--------|
| `AuditEntry` | `Quotinator.Data.Entities` | `Quotinator.Data.Entities` | Generic — stays |
| `Character` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `CharacterTranslation` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `ImportBatch` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `Person` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `QuoteEntity` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `QuoteGenreEntity` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `QuoteTranslationEntity` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `Source` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `SourceTranslation` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |
| `Genre` | `Quotinator.Data.Entities` | `Quotinator.Core.Models` | Domain enum |
| `QuoteType` | `Quotinator.Data.Entities` | `Quotinator.Core.Models` | Domain enum |
| `ImportBatchType` | `Quotinator.Data.Entities` | `Quotinator.Engine.Entities` | Domain-specific |

`SqliteImportBatchRepository` in Data uses the `ImportBatch` entity and implements `IImportBatchRepository`. Both move to Engine — `SqliteImportBatchRepository` to `Quotinator.Engine/Repositories/`.

---

### `Quotinator.Data` after this issue — what remains

Only fully generic, domain-agnostic types:

| Folder | Contents |
|--------|----------|
| `Connections/` | `IDbConnectionFactory`, `SqliteConnectionFactory` |
| `Database/` | `DatabaseInitializer` (abstract base), `DatabaseConfiguration` (abstract base), `DatabaseOptions`, `IDatabaseInitializer`, `SchemaMigration`, `AuditMigrations` |
| `Diagnostics/` | `SqlAggregateGuard` |
| `Entities/` | `AuditEntry` only |
| `Helpers/` | `DapperConfiguration` (removed — replaced by `DatabaseConfiguration`), `GuidHandler`, `SafeDateHandler`, `SafeEnumHandler<T>` |
| `Import/` | `DuplicateResolutionPolicy`, `ManifestPolicy`, `SeedBatch`, `SeedDuplicateRecord`, `SeedPreviewResult` |
| `Models/` | `AuditPageResult`, `RecordBase`, `SafeValue<T>`, `WidgetWithOwner` (example) |
| `Paths/` | `DataPaths` |
| `Queries/` | `IJoinStrategy`, `Sql` (generic parts only: `Schema`, `Joins`, `Queries`, `Audit`), `WidgetWithOwnerStrategy` |
| `Repositories/` | All generic base classes; `IImportBatchRepository` removed (moves to Engine); `SqliteImportBatchRepository` removed (moves to Engine) |

---

### `Quotinator.Engine.Tests` (new test project — `tests/Quotinator.Engine.Tests/`)

Per the testing policy (one test project per source project), `Quotinator.Engine` requires a paired test project.

**Project references:** `Quotinator.Engine`, `Quotinator.Data.Testing` (for `InMemoryTestDatabase` and related helpers)  
**NuGet packages:** `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`, `Dapper` (for test setup only)

**Contents:**

| File | Description |
|------|-------------|
| `CVE/.gitkeep` | CVE folder placeholder (required by testing policy) |
| `QuoteService/` | Integration tests for `SqliteQuoteService` — GetById, GetRandom, GetAll, Search, with filters; moved from `Quotinator.Core.Tests` |
| `DatabaseInitializer/` | Integration tests for `QuotinatorDatabaseInitializer` — migration application, seeding, idempotency |

`Quotinator.Engine.Tests` must be added to `Quotinator.slnx` as a project node under `/tests/`.

A corresponding `/CVE/Quotinator.Engine/` and `/CVE/Quotinator.Engine.Tests/` solution folder pair must be added to `Quotinator.slnx` (with `.gitkeep` files), matching the pattern of all other projects.

---

## Approach

1. **Create `src/Quotinator.Engine/`** — add `.csproj`, register in solution, set up project references to Core and Data.
2. **Restructure `DatabaseConfiguration` in Data** — make abstract; add `RegisterDomainHandlers()` virtual hook and `RegisterEnumHandler<TEnum>()` protected helper.
3. **Refactor `DatabaseInitializer` in Data** — extract generic migration-running base; move Quotinator-specific seeding to a virtual hook (`SeedAsync`); make class abstract or provide empty default implementations.
4. **Move domain enums to Core** — `Genre`, `QuoteType` to `Quotinator.Core.Models`; update all `using` directives.
5. **Move domain import types to Core** — `SourceQuote`, `SourceQuoteTranslation` to `Quotinator.Core.Import`; update `QuoteService.cs` using directive.
6. **Move domain entities to Engine** — all entities except `AuditEntry` to `Quotinator.Engine.Entities`; update all callers.
7. **Move `SqliteImportBatchRepository` and `IImportBatchRepository` to Engine** — update `Quotinator.Api.csproj` references.
8. **Create `QuotinatorDapperConfiguration` in Engine** — extends `DatabaseConfiguration`; registers `Genre` and `QuoteType` enum handlers.
9. **Create `QuotinatorDatabaseInitializer` in Engine** — extends `DatabaseInitializer`; provides `QuotinatorMigrations.All`; implements `SeedAsync` using `IRepository<T>` interfaces.
10. **Move `QuotinatorMigrations` to Engine** — update namespace; update `QuotinatorDatabaseInitializer`.
11. **Move `SqliteQuoteService` to Engine** — update namespace; create or extend repository class(es) in Engine for multi-join quote queries; update `SqliteQuoteService` to use them.
12. **Update `Quotinator.Core.csproj`** — remove Data project reference, Dapper package, Microsoft.Data.Sqlite package.
13. **Update `Quotinator.Api/Program.cs`** — update DI registrations to use Engine types; add Engine project reference.
14. **Update `Quotinator.slnx`** — add Engine project; reflect all file moves.
15. **Update `SqlQueryGuardTests`** — any SQL classes that move have their guard tests updated to follow.
16. **Build and test** — 0 warnings, 0 errors; all tests pass.
17. **Update ADR 004** — revise to reflect new dependency direction and Engine project boundary.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `grep -rn "using Dapper" src/Quotinator.Core/` returns no matches | Shell | Command returns no source-file matches |
| 2 | ✅ | Dapper absent from `Quotinator.Core.csproj` | Code review | No `Dapper` or `Microsoft.Data.Sqlite` in package references |
| 3 | ✅ | `Quotinator.Core.csproj` has no project reference to `Quotinator.Data` | Code review | ProjectReference entry removed |
| 4 | ✅ | `Quotinator.Engine` project exists; builds clean | Build | `dotnet build --configuration Release` — 0 errors |
| 5 | ✅ | `Quotinator.Engine` references Core and Data; is not referenced by either | Code review | `.csproj` project references correct; Core and Data `.csproj` do not reference Engine |
| 6 | ✅ | `Genre`, `QuoteType` in `Quotinator.Core.Models` | Build | `dotnet build --configuration Release` — 0 errors |
| 7 | ✅ | `SourceQuote`, `SourceQuoteTranslation` in `Quotinator.Core.Import` | Build | `dotnet build --configuration Release` — 0 errors |
| 8 | ✅ | Domain entities (`Character`, `Source`, `QuoteEntity`, etc.) in `Quotinator.Engine.Entities` | Build | `dotnet build --configuration Release` — 0 errors |
| 9 | ✅ | `AuditEntry` remains in `Quotinator.Data.Entities`; Data has no other domain entities | Code review | `src/Quotinator.Data/Entities/` contains only `AuditEntry.cs` |
| 10 | ✅ | `DatabaseConfiguration` abstract base exists in Data with `RegisterEnumHandler<TEnum>()` helper | Build | `dotnet build --configuration Release` — 0 errors |
| 11 | ✅ | `QuotinatorDapperConfiguration` in Engine extends `DatabaseConfiguration`; registers `Genre` and `QuoteType` handlers | Build | `dotnet build --configuration Release` — 0 errors |
| 12 | ✅ | `DatabaseInitializer` base in Data has virtual seeding hooks; domain seeding overridden by Engine subclass | Code review | `OnInitialisedAsync`, `OnReseedAsync`, `OnResetAsync` are protected virtual |
| 13 | ✅ | `QuotinatorDatabaseInitializer` in Engine extends `DatabaseInitializer` and provides domain seeding | Build | `dotnet build --configuration Release` — 0 errors (note: Engine seeder uses Dapper internally, which is correct for an Engine-layer class) |
| 14 | ✅ | `SqliteQuoteService` in Engine implements `IQuoteService` | Build | `dotnet build --configuration Release` — 0 errors |
| 15 | ✅ | `Quotinator.Data` contains no Quotinator-domain types (Quotes, Sources, Persons, etc.) | Code review | `src/Quotinator.Data/Entities/` contains only `AuditEntry.cs`; no domain entity references remain |
| 16 | ✅ | `Quotinator.Api/Program.cs` has no `using Dapper` or `using Microsoft.Data.Sqlite` | Shell | `grep` returns no output |
| 17 | ✅ | ADR 004 updated to reflect Engine project and revised dependency direction | Code review | `docs/architecture-decisions/004-quotinator-data-project-boundaries.md` updated 2026-06-28 |
| 18 | ✅ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` — 0 Warning(s) 0 Error(s) |
| 19 | ✅ | All tests pass | Tests | `dotnet test --configuration Release` — 558 passed, 0 failed |
| 20 | ✅ | `Quotinator.Engine.Tests` project exists; paired with Engine; CVE folder present | Code review | `tests/Quotinator.Engine.Tests/` exists; `CVE/.gitkeep` present; project in `Quotinator.slnx` |
| 21 | ✅ | Integration tests in Engine.Tests pass | Tests | `dotnet test --filter "FullyQualifiedName~Quotinator.Engine.Tests"` — 13 passed |
| 22 | ✅ | App starts without error; quotes load correctly | T1 | Schema v4, 788 quotes / 478 sources confirmed; reset endpoint tested; startup banner clean — 2026-06-28 |
| 23 | ✅ | Docker build succeeds | T2 | `docker build -f docker/Dockerfile -t quotinator:local .` — clean build 2026-06-28; also fixed Dockerfile to include `Quotinator.Engine` and `Quotinator.Changelog` in restore layer |
