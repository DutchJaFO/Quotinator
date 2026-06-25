# Issue #74 — Add read-model query pattern to Quotinator.Data for join and projection queries

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

`IRepository<T>` assumes a single flat table. Queries that join two or more tables, or that return a projection (a subset or combination of columns), cannot be expressed through it:
- `SELECT * FROM {TableName}` returns one table's columns; a join brings in a second table with overlapping column names (`Id`, `IsDeleted`, `DateCreated`) that collide in the Dapper mapping.
- `T` is a `RecordBase` entity — it has no place to hold columns from a joined table.
- Forcing joins through `IRepository` would break its single-responsibility.

## Approach

Introduce a **read-model query service** alongside (not inside) `IRepository`:
- A *read model* is a plain class (not a `RecordBase`, not a table entity) shaped to match the result of a specific query.
- A *query service* holds the hand-written Dapper query and returns the read model.
- `IRepository` is unchanged.

## Scope

1. Define the convention in `Quotinator.Data` (folder structure, naming, base class or marker interface if needed)
2. Add at least one canonical implementation when the first real join query is needed
3. Document the pattern in `docs/data-access.md` or `docs/sql-safety.md`
4. Add tests for the first concrete query service

---

## Design decisions to make at implementation time

- **Folder**: likely `Quotinator.Data/Queries/` or `Quotinator.Data/ReadModels/` — decide based on what exists when this is worked
- **Base class**: a bare POCO class is sufficient; a marker interface (`IReadModel`) may help with discoverability but is not required
- **Multi-mapping**: evaluate Dapper's `QueryAsync<T1, T2, TResult>` vs single-column projection at implementation time
- **SQL placement**: all SQL must live in `Sql.cs` as constants or factory methods — the read-model class holds no SQL

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Convention documented (`docs/data-access.md` or `docs/sql-safety.md`) | Code review | Doc exists; describes read-model naming, folder, and SQL placement rules |
| 2 | ⬜ | At least one concrete read-model class and query service added | Code review | Class in correct folder; no `RecordBase` inheritance; SQL in `Sql.cs` |
| 3 | ⬜ | Integration test covers the first concrete query service | Unit test | Test class + method names in verification (TBD at implementation) |
| 4 | ⬜ | `SqlSourceScanTests` still passes | Unit test | `SqlSourceScanTests` — all pass |
| 5 | ⬜ | Build clean — 0 warnings, 0 errors | Live | `dotnet build --configuration Release` |
| 6 | ⬜ | All tests pass | Live | `dotnet test --configuration Release` |
| 7 | ⬜ | User manual test — app starts without error | Live | User starts app in VS; confirms startup without error |
