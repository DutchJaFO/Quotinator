# Issue #76 — Add 1:1 relationship pattern to Quotinator.Data

**Milestone:** v1.7.0  
**Status:** Open  
**Branch:** `claude/7-0-milestone-issues-8ubkfo`

---

## Problem

A one-to-one relationship pairs exactly one row in a primary table with exactly one row in a secondary table. `IRepository<T>` maps `T` to one table — there is no mechanism to load or write a paired row, or enforce the at-most-one-child constraint.

## Two layouts to formalise

### Shared primary key (tight coupling)

Child shares the parent's `Id` as its own primary key and foreign key. Parent and child are always created and deleted together.

```sql
CREATE TABLE Quotes      (Id TEXT PRIMARY KEY, ...);
CREATE TABLE QuoteDetail (Id TEXT PRIMARY KEY REFERENCES Quotes(Id), ExtraField TEXT);
```

### Separate primary key (loose coupling)

Child has its own `Id` and a nullable foreign key back to the parent. Child can exist independently or be attached later.

```sql
CREATE TABLE Quotes      (Id TEXT PRIMARY KEY, ...);
CREATE TABLE QuoteDetail (Id TEXT PRIMARY KEY, QuoteId TEXT REFERENCES Quotes(Id), ExtraField TEXT);
```

## Depends on

- **#73** (audit trail) — `IAuditWriter` and `ICallerContext` are required on any `SqliteRepository<T>` derivation; all write paths route through the base class
- **#74** (read-model query pattern) — `IUnitOfWork` is the established transaction coordination mechanism; `IRepository<T>` already has `IUnitOfWork? unitOfWork = null` on all mutating methods
- **#75** (master/detail) — the transactional two-table write pattern is identical in structure; implement #75 first

## Scope

1. Define and document the convention for shared-PK vs separate-PK layouts in `Quotinator.Data`
2. Add a concrete implementation when the first 1:1 entity pair is needed in a milestone
3. All write methods use `IUnitOfWork` for transaction coordination — `IDbTransaction` is not used directly (resolved in #74/#75)
4. Integration tests: insert both sides in one transaction; load child by parent id; rollback leaves neither row

---

## Implementation notes

- Concrete repository constructor must accept `IAuditWriter` and `ICallerContext` and pass them to `base`
- All inserts route through `base.InsertAsync(entity, uow)` or a child `SqliteRepository<T>.InsertAsync(entity, uow)` — never call Dapper directly inside an override
- For loading (`GetDetailAsync`), use a single-table query (by parent-Id or by shared PK) — no join needed for 1:1 reads; use `JoinQueryRepository` only if a denormalised read model is required
- Soft-delete strategy for the pair must be documented per use case: either cascade (both sides soft-deleted together) or independent (only parent is soft-deleted; child remains queryable)

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Shared-PK vs separate-PK convention documented with when-to-use guidance and soft-delete strategy | Code review | `docs/data-access.md` — both layouts described |
| 2 | ⬜ | Concrete 1:1 repository exists; constructor takes `IAuditWriter` and `ICallerContext` | Code review | Class compiles; no raw Dapper calls inside write overrides |
| 3 | ⬜ | Insert both sides in one `IUnitOfWork` transaction succeeds | Integration test | Test class + method (TBD at implementation) |
| 4 | ⬜ | Both inserts produce audit entries in `AuditEntries` | Integration test | Test class + method (TBD at implementation) |
| 5 | ⬜ | Rollback leaves neither row (and no stale audit entry) | Integration test | Test class + method (TBD at implementation) |
| 6 | ⬜ | Load child by parent id works | Integration test | Test class + method (TBD at implementation) |
| 7 | ⬜ | Build clean — 0 warnings, 0 errors | Build | `dotnet build --configuration Release` |
| 8 | ⬜ | All tests pass | Build | `dotnet test --configuration Release` |
| 9 | ⬜ | App starts without error | T1 | User starts app in VS; confirms startup banner |
