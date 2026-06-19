# #71 — Generic repository pattern for database entities

**Status:** In progress  
**GitHub issue:** #71  
**Unblocks:** #58 (ImportBatches), all future entity repositories

---

## Scope decision: Option B

The first concrete implementation (`IImportBatchRepository` / `SqliteImportBatchRepository`)
is deferred to issue #58, which owns the `ImportBatch` entity and schema migration.
This issue delivers only the base infrastructure in `Quotinator.Data`.

**Reason:** The `ImportBatch` entity does not exist yet — #58 adds it alongside the schema
migration. Coupling #71 to #58 would make this issue impossible to close independently.
Shipping the infrastructure now keeps #71 self-contained and testable.

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

See [`docs/sql-safety.md`](../../../sql-safety.md) for the full design rationale.
See [`docs/architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md`](../../../architecture-decisions/001-cve-2025-6965-sql-aggregate-guard.md) for the Quotinator-level decision record.

---

## Implementation steps

- [ ] Add `IRepository<T>` interface to `Quotinator.Data/Repositories/`
- [ ] Add `SqliteRepository<T>` base class to `Quotinator.Data/Repositories/`
- [ ] Add `SqlAggregateGuard` to `Quotinator.Data/Diagnostics/`
- [ ] Create `tests/Quotinator.Data.Tests` project
- [ ] Add `SqliteRepositoryTests` — CRUD round-trip against in-memory SQLite
- [ ] Add `SqlAggregateGuardTests` — detector unit tests (known-dangerous and known-safe cases)
- [ ] Add `SqlSourceScanTests` to `Quotinator.Core.Tests` — scans `src/` for vulnerable patterns
- [ ] Add new test project to `Quotinator.slnx`
- [ ] Update `docs/README.md`, `docs/sql-safety.md`, `docs/architecture-decisions/001-...`

---

## Verification

| # | Status | Requirement | Test |
|---|--------|-------------|------|
| 1 | ❌ | `IRepository<T>` interface exists with correct members | `SqliteRepositoryTests.IRepository_HasRequiredMembers` |
| 2 | ❌ | `SqliteRepository<T>` implements `IRepository<T>` | `SqliteRepositoryTests.SqliteRepository_ImplementsIRepository` |
| 3 | ❌ | `InsertAsync` writes a record readable via `GetByIdAsync` | `SqliteRepositoryTests.InsertAsync_ThenGetById_ReturnsRecord` |
| 4 | ❌ | `UpdateAsync` persists changes | `SqliteRepositoryTests.UpdateAsync_PersistsChanges` |
| 5 | ❌ | `SoftDeleteAsync` sets `IsDeleted=1` and `DateDeleted`; `GetByIdAsync` returns null | `SqliteRepositoryTests.SoftDeleteAsync_HidesRecord` |
| 6 | ❌ | Guard flags GROUP BY + aggregate as dangerous | `SqlAggregateGuardTests.GroupByWithAggregate_IsFlagged` |
| 7 | ❌ | Guard passes simple COUNT(*) as safe | `SqlAggregateGuardTests.SimpleCountStar_IsSafe` |
| 8 | ❌ | Guard flags SQLite-specific GROUP_CONCAT with GROUP BY | `SqlAggregateGuardTests.GroupConcatWithGroupBy_IsFlagged` |
| 9 | ❌ | Guard flags HAVING with aggregate | `SqlAggregateGuardTests.HavingWithAggregate_IsFlagged` |
| 10 | ❌ | Guard passes MAX without GROUP BY | `SqlAggregateGuardTests.MaxWithoutGroupBy_IsSafe` |
| 11 | ❌ | All SQL in `src/` passes the aggregate guard | `SqlSourceScanTests.AllSqlInSourceFiles_NoVulnerableAggregatePatterns` |

---

## Notes

Bulk seeding in `DatabaseInitializer` uses a shared connection across many inserts for performance.
Repository methods open their own connection per operation and are not suitable for bulk use —
those stay inline in `DatabaseInitializer` for now.

`GetByIdAsync` uses Dapper.Contrib `GetAsync<T>` then filters `IsDeleted` in C# — no raw SQL.
`SoftDeleteAsync` uses a hand-written `UPDATE` parameterised on `Id`; the table name comes from
the `[Table]` attribute on `T` (developer-controlled metadata, not user input — not a SQL injection
risk). This is the only raw SQL in `SqliteRepository<T>`.
