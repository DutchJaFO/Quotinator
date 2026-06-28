# Issue #76 — Add 1:1 relationship pattern to Quotinator.Data

**Issue:** https://github.com/DutchJaFO/Quotinator/issues/76  
**Milestone:** v1.7.0  
**Status:** 🔴 Open

---

## Depends on

- **#73** (audit trail) — `IAuditWriter` and `ICallerContext` are required on any `SqliteRepository<T>` derivation; all write paths route through the base class
- **#74** (read-model query pattern) — `IUnitOfWork` is the established transaction coordination mechanism
- **#75** (master/detail) — `AggregateRepository<TParent, TChild>` handles the write side; `TransactionScope` is the coordination primitive

---

## Problem

A one-to-one relationship pairs exactly one row in a primary table with exactly one row in a secondary table. `IRepository<T>` maps `T` to one table — there is no mechanism to write a paired row atomically or load the detail record by parent ID.

---

## Two layouts

### Shared primary key (tight coupling)

Child shares the parent's `Id` as its own PK. Parent and child are always created and deleted together.

```sql
CREATE TABLE Widgets      (Id TEXT PRIMARY KEY, Label TEXT NOT NULL, ...);
CREATE TABLE WidgetDetails(Id TEXT PRIMARY KEY REFERENCES Widgets(Id), Notes TEXT NOT NULL, ...);
```

`GetDetailAsync` → `ChildRepository.GetByIdAsync(parentId)` (same key).

### Separate foreign key (loose coupling)

Child has its own `Id` and a nullable FK back to the parent. Child can exist independently or be attached later.

```sql
CREATE TABLE Widgets        (Id TEXT PRIMARY KEY, Label TEXT NOT NULL, ...);
CREATE TABLE WidgetDetailsFk(Id TEXT PRIMARY KEY, WidgetId TEXT REFERENCES Widgets(Id), Notes TEXT NOT NULL, ...);
```

`GetDetailAsync` → `RepositorySql.SelectByForeignKey(tableName, fkColumn)` query.

---

## `IOneToOneRepository<TParent, TDetail>`

```csharp
public interface IOneToOneRepository<TParent, TDetail> : IRepository<TParent>
    where TParent : RecordBase
    where TDetail : RecordBase
{
    Task<TDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? unitOfWork = null);
}
```

---

## `SqliteOneToOneRepository<TParent, TDetail>`

Extends `AggregateRepository<TParent, TDetail>` (write side already handled) and implements `IOneToOneRepository`. Provides two protected helpers so concrete subclasses only need to pick the right one for their layout:

```csharp
public abstract class SqliteOneToOneRepository<TParent, TDetail>(...) 
    : AggregateRepository<TParent, TDetail>(...), IOneToOneRepository<TParent, TDetail>
    where TParent : RecordBase
    where TDetail : RecordBase
{
    // Resolve TDetail table name once — same pattern as SqliteRepositoryBase.TableName
    private static readonly string DetailTableName = ...;

    public abstract Task<TDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? unitOfWork = null);

    // For shared-PK layouts: detail.Id == parent.Id
    protected Task<TDetail?> GetDetailBySharedKeyAsync(Guid parentId, IUnitOfWork? unitOfWork = null)
        => ChildRepository.GetByIdAsync(parentId, unitOfWork);

    // For separate-FK layouts: WHERE fkColumn = @parentId AND IsDeleted = 0
    protected async Task<TDetail?> GetDetailByForeignKeyAsync(
        string fkColumn, Guid parentId, IUnitOfWork? unitOfWork = null)
    { ... }
}
```

`ChildInsertStrategy` defaults to `Bulk` (inherited from `AggregateRepository`); 1:1 always has exactly one child, so the strategy choice has no practical effect.

---

## `RepositorySql.SelectByForeignKey`

```csharp
internal static string SelectByForeignKey(string tableName, string fkColumn)
    => $"SELECT * FROM [{tableName}] WHERE [{fkColumn}] = @parentId AND [IsDeleted] = 0";
```

Table name and FK column name come from `[Table]` attributes and developer-provided constants — not user input. Guard test added to `RepositorySqlGuardTests`.

---

## Soft-delete strategy (per use case)

The pattern does not prescribe a default soft-delete strategy — the concrete repository documents its own choice:

| Strategy | When to use |
|----------|-------------|
| **Cascade** — soft-delete parent also soft-deletes the detail in one `TransactionScope` | Parent and detail are always queried together; a soft-deleted parent should never return a live detail |
| **Independent** — only the parent is soft-deleted; detail remains active | Detail has a meaningful independent lifetime, or is queried separately |

Cascade soft-delete is the expected pattern for most 1:1 relationships. Document the choice in the concrete repository class.

---

## Approach

1. Add `RepositorySql.SelectByForeignKey(tableName, fkColumn)` and guard test case
2. Create `IOneToOneRepository<TParent, TDetail>` in `Quotinator.Data`
3. Create `SqliteOneToOneRepository<TParent, TDetail>` abstract base in `Quotinator.Data`
4. Update `docs/data-access.md` — document both layouts, helpers, and soft-delete strategy table
5. Add integration tests in `Quotinator.Data.Tests` covering both layouts

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Shared-PK vs separate-PK convention documented with when-to-use guidance and soft-delete strategy | Code review | `docs/data-access.md` — both layouts described, helper methods documented |
| 2 | ⬜ | `IOneToOneRepository<TParent, TDetail>` interface exists with `GetDetailAsync` | Build | `dotnet build --configuration Release` — 0 errors |
| 3 | ⬜ | `SqliteOneToOneRepository<TParent, TDetail>` abstract base exists; extends `AggregateRepository`; provides `GetDetailBySharedKeyAsync` and `GetDetailByForeignKeyAsync` helpers | Build | `dotnet build --configuration Release` — 0 errors |
| 4 | ⬜ | `RepositorySql.SelectByForeignKey` passes CVE aggregate guard | Unit test | `RepositorySqlGuardTests.RepositorySqlFactory_PassesAggregateGuard["SelectByForeignKey"]` |
| 5 | ⬜ | Shared PK: insert parent+detail in one transaction — both rows committed | Integration test | `OneToOneRepositoryTests.SharedPk_Insert_BothRowsCommitted` |
| 6 | ⬜ | Shared PK: both inserts produce audit entries | Integration test | `OneToOneRepositoryTests.SharedPk_Insert_AuditEntriesForBoth` |
| 7 | ⬜ | Shared PK: rollback leaves neither row | Integration test | `OneToOneRepositoryTests.SharedPk_Insert_Rollback_NeitherRowPersists` |
| 8 | ⬜ | Shared PK: `GetDetailAsync` returns the detail by parent ID | Integration test | `OneToOneRepositoryTests.SharedPk_GetDetailAsync_ReturnsDetail` |
| 9 | ⬜ | Shared PK: `GetDetailAsync` returns null when no detail exists | Integration test | `OneToOneRepositoryTests.SharedPk_GetDetailAsync_ReturnsNull_WhenNoDetail` |
| 10 | ⬜ | Separate FK: insert parent+detail in one transaction — both rows committed | Integration test | `OneToOneRepositoryTests.SeparateFk_Insert_BothRowsCommitted` |
| 11 | ⬜ | Separate FK: both inserts produce audit entries | Integration test | `OneToOneRepositoryTests.SeparateFk_Insert_AuditEntriesForBoth` |
| 12 | ⬜ | Separate FK: rollback leaves neither row | Integration test | `OneToOneRepositoryTests.SeparateFk_Insert_Rollback_NeitherRowPersists` |
| 13 | ⬜ | Separate FK: `GetDetailAsync` returns the detail by FK column | Integration test | `OneToOneRepositoryTests.SeparateFk_GetDetailAsync_ReturnsDetail` |
| 14 | ⬜ | Separate FK: `GetDetailAsync` returns null when no detail exists | Integration test | `OneToOneRepositoryTests.SeparateFk_GetDetailAsync_ReturnsNull_WhenNoDetail` |
| 15 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 16 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 17 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |

### T1 / T2 / T3

| Tier | Required | Items |
|------|----------|-------|
| T1 | ✅ Required | App starts in VS without error; startup banner shows no exceptions |
| T2 | ✅ Required | `docker build -f docker/Dockerfile -t quotinator:local .` succeeds |
| T3 | ➖ Not required | No HA-specific behaviour — pure `Quotinator.Data` infrastructure |
