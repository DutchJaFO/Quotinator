# #71 — Generic repository pattern for database entities

**Status:** Released
**GitHub issue:** #71  
**Unblocks:** #58 (ImportBatches), all future entity repositories

---

## Scope decision: Option B

This issue delivers only the base infrastructure in `Quotinator.Data`:
`IRepository<T>` and `SqliteRepository<T>`.

**Reason:** The `ImportBatch` entity does not exist yet — #58 adds it alongside the schema
migration. Coupling #71 to #58 would make this issue impossible to close independently.
Shipping the infrastructure now keeps #71 self-contained and testable.

---

## Scope changes

The original GitHub issue spec included:

> - Add first concrete implementation: `IImportBatchRepository` / `SqliteImportBatchRepository` in `Quotinator.Core`
> - Register in DI

These items are **deferred to #58**, which owns the `ImportBatch` entity, the schema
migration, and the decision of whether the concrete repository needs to subclass
`SqliteRepository<T>` or simply use `IRepository<ImportBatch>` via DI directly.

A comment on GitHub issue #71 documents this deferral.

---

## Spec requirements (revised)

1. `IRepository<T>` interface in `Quotinator.Data` with `GetByIdAsync`, `InsertAsync`, `UpdateAsync`, `SoftDeleteAsync`
2. `SqliteRepository<T>` base class in `Quotinator.Data` implementing `IRepository<T>` using Dapper/Dapper.Contrib
3. Repository methods open their own connection via `IDbConnectionFactory` — no shared-connection coupling
4. `SqliteRepository<T>` exposes **no raw SQL surface** — all four methods use Dapper.Contrib
5. New `Quotinator.Data.Tests` project with CRUD round-trip tests and SQL aggregate guard tests
6. `SqlAggregateGuard` utility in `Quotinator.Data` (see below)
7. Existing inline inserts in `DatabaseInitializer` are **not** migrated in this issue — tracked separately

*Deferred to #58:* `IImportBatchRepository`, `SqliteImportBatchRepository`, DI registration.

---

## SQL aggregate guard (added to this issue's scope)

CVE-2025-6965 affects `SQLitePCLRaw.lib.e_sqlite3` ≤ 2.1.11. No patched version is available.
A `SqlAggregateGuard` utility and supporting tests are introduced here as the preventive guardrail.

See [`docs/sql-safety.md`](../../sql-safety.md) for the full design rationale.
See [`docs/architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md`](../../architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md) for the Quotinator-level decision record.

---

## Implementation steps

1. [x] Add `IRepository<T>` interface to `Quotinator.Data/Repositories/`
2. [x] Add `SqliteRepository<T>` base class to `Quotinator.Data/Repositories/`
3. [x] Add `SqlAggregateGuard` to `Quotinator.Data/Diagnostics/`
4. [x] Create `tests/Quotinator.Data.Tests` project
5. [x] Add `SqliteRepositoryTests` — CRUD round-trip against file-based SQLite
6. [x] Add `SqlAggregateGuardTests` — detector unit tests (known-dangerous and known-safe cases)
7. [x] Add `SqlSourceScanTests` to `Quotinator.Core.Tests` — scans `src/` for vulnerable patterns
8. [x] Add new test project to `Quotinator.slnx`
9. [x] Update `docs/README.md`, `docs/sql-safety.md`, `docs/architecture-decisions/001-...`

---

## Scope additions

The following were built beyond the original spec and are covered in the verification table below:

- `IRestorableRepository<T>` — extends `IRepository<T>` with soft-delete recovery: `GetDeletedAsync`, `RestoreAsync`, `HardDeleteAsync`, `PurgeAsync`
- `SqliteRestorableRepository<T>` — extends `SqliteRepository<T>` and implements `IRestorableRepository<T>`
- `RepositorySql.cs` — centralised SQL constants and factory methods for repository queries, verified against the aggregate guard

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `IRepository<T>` interface exists with correct members (`GetByIdAsync`, `InsertAsync`, `UpdateAsync`, `SoftDeleteAsync`) | Unit test | `SqliteRepositoryTests.SqliteRepository_ImplementsIRepository` — compile-time contract; interface must be satisfied for the test to build and run |
| 2 | ✅ | `SqliteRepository<T>` implements `IRepository<T>` | Unit test | `SqliteRepositoryTests.SqliteRepository_ImplementsIRepository` |
| 3 | ✅ | `InsertAsync` writes a record readable via `GetByIdAsync` | Unit test | `SqliteRepositoryTests.InsertAsync_ThenGetById_ReturnsRecord` |
| 4 | ✅ | `UpdateAsync` persists changes | Unit test | `SqliteRepositoryTests.UpdateAsync_PersistsChanges` |
| 5 | ✅ | `SoftDeleteAsync` sets `IsDeleted=1` and `DateDeleted`; `GetByIdAsync` returns null | Unit test | `SqliteRepositoryTests.SoftDeleteAsync_HidesRecordFromGetById` |
| 6 | ✅ | Guard flags GROUP BY + aggregate as dangerous | Unit test | `SqlAggregateGuardTests.IsVulnerablePattern_GroupByWithCount_ReturnsTrue` |
| 7 | ✅ | Guard passes simple COUNT(*) as safe | Unit test | `SqlAggregateGuardTests.IsVulnerablePattern_SimpleCountStar_ReturnsFalse` |
| 8 | ✅ | Guard flags SQLite-specific GROUP_CONCAT with GROUP BY | Unit test | `SqlAggregateGuardTests.IsVulnerablePattern_GroupByWithGroupConcat_ReturnsTrue` |
| 9 | ✅ | Guard flags HAVING with aggregate | Unit test | `SqlAggregateGuardTests.IsVulnerablePattern_HavingWithCount_ReturnsTrue` |
| 10 | ✅ | Guard passes MAX/COALESCE(MAX) without GROUP BY as safe | Unit test | `SqlAggregateGuardTests.IsVulnerablePattern_CoalesceMax_ReturnsFalse` |
| 11 | ✅ | All SQL in `src/` passes the aggregate guard | Unit test | `SqlSourceScanTests.AllSqlInSourceFiles_NoVulnerableAggregatePatterns` |
| 12 | ✅ | `IRestorableRepository<T>` extends `IRepository<T>` with recovery methods | Unit test | `SqliteRestorableRepositoryTests.SqliteRestorableRepository_ImplementsIRestorableRepository` |
| 13 | ✅ | `GetDeletedAsync` returns only soft-deleted records | Unit test | `SqliteRestorableRepositoryTests.GetDeletedAsync_ReturnsOnlySoftDeletedRecords` |
| 14 | ✅ | `RestoreAsync` makes a soft-deleted record visible via `GetByIdAsync` | Unit test | `SqliteRestorableRepositoryTests.RestoreAsync_MakesRecordVisibleViaGetById` |
| 15 | ✅ | `HardDeleteAsync` permanently removes a soft-deleted record | Unit test | `SqliteRestorableRepositoryTests.HardDeleteAsync_RemovesSoftDeletedRecord` |
| 16 | ✅ | `PurgeAsync` removes all soft-deleted records and returns the count | Unit test | `SqliteRestorableRepositoryTests.PurgeAsync_ReturnsPurgedCount` |
| 17 | ✅ | All SQL in repository classes passes the aggregate guard | Unit test | `RepositorySqlGuardTests.RepositorySqlFactory_PassesAggregateGuard` |

---

## Notes

Bulk seeding in `DatabaseInitializer` uses a shared connection across many inserts for performance.
Repository methods open their own connection per operation and are not suitable for bulk use —
those stay inline in `DatabaseInitializer` for now.

`GetByIdAsync` uses a hand-written `SELECT * FROM {TableName} WHERE Id = @id AND IsDeleted = 0`.
`SoftDeleteAsync` uses a hand-written `UPDATE` parameterised on `Id`; the table name in both cases
comes from the `[Table]` attribute on `T` (developer-controlled metadata, not user input — not a
SQL injection risk). `InsertAsync` and `UpdateAsync` delegate to Dapper.Contrib.

**Guid storage format:** Microsoft.Data.Sqlite stores `Guid` values as uppercase TEXT by default
(e.g. `"A3F2..."`). `GuidHandler` matches this format so that all paths — Dapper.Contrib inserts,
Dapper reads, and hand-written WHERE clauses — use the same uppercase representation. This handler
is registered in `DapperConfiguration.Configure()` (production) and in the test `ClassInitialize`.
